using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Models;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace DiskChecker.UI.WPF.ViewModels;

public partial class ReportViewModel : ViewModelBase
{
   private readonly HistoryService _historyService;

   [ObservableProperty]
   private int totalTests;

   [ObservableProperty]
   private int smartCheckTests;

   [ObservableProperty]
   private int surfaceTests;

   [ObservableProperty]
   private double averageScore;

   [ObservableProperty]
   private string bestGrade = "-";

   [ObservableProperty]
   private string? lastDriveName;

   [ObservableProperty]
   private DateTime? lastTestDate;

   [ObservableProperty]
   private ObservableCollection<ReportSummaryItem> recentTests = [];

   [ObservableProperty]
   private TestHistoryItem? selectedTest;

   [ObservableProperty]
   private bool showTestDetail;

   [ObservableProperty]
   private GridLength showTestDetailWidth = new GridLength(0);

   [ObservableProperty]
   private PlotModel? speedPlotModel;

   [ObservableProperty]
   private string? testDetailInfo;

   [ObservableProperty]
   private string? testDetailStats;

   public ReportViewModel(HistoryService historyService)
   {
      _historyService = historyService;
      StatusMessage = "Přehled reportů připraven.";
   }

   [RelayCommand]
   public async Task RefreshReportAsync()
   {
      IsBusy = true;
      StatusMessage = "📄 Načítám souhrn reportů...";

      var history = await _historyService.GetForCompareAsync(100);

      TotalTests = history.Count;
      SmartCheckTests = history.Count(i => i.TestType.Contains("Smart", StringComparison.OrdinalIgnoreCase));
      SurfaceTests = history.Count(i => i.TestType.Contains("Surface", StringComparison.OrdinalIgnoreCase));
      AverageScore = history.Count == 0 ? 0 : history.Average(i => i.Score);

      if(history.Count > 0)
      {
         var last = history.OrderByDescending(i => i.TestDate).First();
         LastDriveName = last.DriveName;
         LastTestDate = last.TestDate;
         BestGrade = history.Min(i => i.Grade).ToString();
      }
      else
      {
         LastDriveName = null;
         LastTestDate = null;
         BestGrade = "-";
      }

      RecentTests = new ObservableCollection<ReportSummaryItem>(history
          .OrderByDescending(i => i.TestDate)
          .Take(50)
          .Select(i => new ReportSummaryItem
          {
             TestId = i.TestId,
             TestDate = i.TestDate,
             DriveName = i.DriveName,
             TestType = i.TestType,
             Grade = i.Grade.ToString(),
             Score = i.Score,
             ErrorCount = i.ErrorCount
          }));

      StatusMessage = history.Count == 0
          ? "Zatím nejsou k dispozici žádné reporty."
          : $"✅ Načteno {history.Count} testů do reportu.";
      IsBusy = false;
      ShowTestDetail = false;
   }

   [RelayCommand]
   public async Task ShowTestDetailAsync(Guid testId)
   {
      IsBusy = true;
      StatusMessage = "📊 Načítám detail testu...";

      var test = await _historyService.GetTestByIdAsync(testId);
      if(test == null)
      {
         StatusMessage = "❌ Test nebyl nalezen.";
         IsBusy = false;
         return;
      }

      SelectedTest = test;

      TestDetailInfo = $"Model: {test.DriveName}\n" +
                       $"Sériové číslo: {test.SerialNumber}\n" +
                       $"Datum testu: {test.TestDate:dd.MM.yyyy HH:mm}\n" +
                       $"Typ: {test.TestType}\n" +
                       $"Známka: {test.Grade}\n" +
                       $"Skóre: {test.Score:F1}/100";

      TestDetailStats = $"Průměr: {test.AverageSpeed:F1} MB/s\n" +
                         $"Maximum: {test.PeakSpeed:F1} MB/s\n" +
                         $"Minimum: {test.MinSpeed:F1} MB/s\n" +
                         $"Chyby: {test.ErrorCount}\n" +
                         $"Testováno: {FormatBytes(test.TotalBytesTested)}";

      if(test.SurfaceSamples != null && test.SurfaceSamples.Count > 0)
      {
         CreateSpeedPlot(test.SurfaceSamples.ToList());
      }
      else
      {
         SpeedPlotModel = null;
      }

      ShowTestDetail = true;
      ShowTestDetailWidth = new GridLength(450);
      StatusMessage = $"✅ Detail testu načten.";
      IsBusy = false;
   }

   [RelayCommand]
   public void CloseTestDetail()
   {
      ShowTestDetail = false;
      ShowTestDetailWidth = new GridLength(0);
      SpeedPlotModel = null;
   }

   private void CreateSpeedPlot(System.Collections.Generic.IReadOnlyList<SurfaceTestSample> samples)
   {
      var model = new PlotModel
      {
         Title = "Průběh rychlosti testu",
         Background = OxyColors.White,
         PlotAreaBorderColor = OxyColors.Gray
      };

      model.Axes.Add(new LinearAxis
      {
         Position = AxisPosition.Bottom,
         Title = "Čas (s)",
         MajorGridlineStyle = LineStyle.Dot,
         MinorGridlineStyle = LineStyle.None,
         MajorGridlineColor = OxyColors.LightGray
      });

      model.Axes.Add(new LinearAxis
      {
         Position = AxisPosition.Left,
         Title = "Rychlost (MB/s)",
         MajorGridlineStyle = LineStyle.Dot,
         MinorGridlineStyle = LineStyle.None,
         MajorGridlineColor = OxyColors.LightGray
      });

      var series = new LineSeries
      {
         Title = "Rychlost",
         Color = OxyColors.Green,
         StrokeThickness = 2,
         MarkerType = MarkerType.None
      };

      if(samples.Count > 0 && samples[0].TimestampUtc != default)
      {
         var startTime = samples[0].TimestampUtc;
         foreach(var sample in samples)
         {
            var timeSeconds = (sample.TimestampUtc - startTime).TotalSeconds;
            series.Points.Add(new DataPoint(timeSeconds, sample.ThroughputMbps));
         }
      }
      else
      {
         for(int i = 0; i < samples.Count; i++)
         {
            series.Points.Add(new DataPoint(i, samples[i].ThroughputMbps));
         }
      }

       model.Series.Add(series);
       SpeedPlotModel = model;
   }

   private static string FormatBytes(long bytes)
   {
      string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
      int i = 0;
      double d = bytes;
      while(d >= 1024 && i < suffixes.Length - 1)
      {
         d /= 1024;
         i++;
      }
      return $"{d:F1} {suffixes[i]}";
   }

   public override async Task InitializeAsync()
   {
      await RefreshReportAsync();
   }
}