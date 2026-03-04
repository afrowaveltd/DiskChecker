using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using System.Windows;

namespace DiskChecker.UI.WPF.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
   private readonly HistoryService _historyService;

   [ObservableProperty]
   private ObservableCollection<HistoryListItem> historyItems = [];

   [ObservableProperty]
   private HistoryListItem? selectedItem;

   [ObservableProperty]
   private int totalItems;

   [ObservableProperty]
   private ObservableCollection<string> uniqueDrives = [];

   [ObservableProperty]
   private string? selectedDriveFilter;

   [ObservableProperty]
   private bool showDriveDetail;

   [ObservableProperty]
   private GridLength showDriveDetailWidth = new GridLength(0);

   [ObservableProperty]
   private string? selectedDriveName;

   [ObservableProperty]
   private ObservableCollection<HistoryListItem> driveHistory = [];

   [ObservableProperty]
   private PlotModel? trendPlotModel;

   [ObservableProperty]
   private string? driveSummary;

   public HistoryViewModel(HistoryService historyService)
   {
      _historyService = historyService;
      StatusMessage = "Historie testů připravena.";
   }

   [RelayCommand]
   public async Task RefreshHistoryAsync()
   {
      IsBusy = true;
      StatusMessage = "📚 Načítám historii testů...";

      var page = await _historyService.GetHistoryAsync(pageSize: 100, pageIndex: 0);

      HistoryItems = new ObservableCollection<HistoryListItem>(page.Items.Select(i => new HistoryListItem
      {
         TestId = i.TestId,
         TestDate = i.TestDate,
         DriveName = i.DriveName,
         SerialNumber = i.SerialNumber,
         TestType = i.TestType,
         Grade = i.Grade.ToString(),
         Score = i.Score,
         ErrorCount = i.ErrorCount
      }));

      var drives = await _historyService.GetDrivesWithTestsAsync();
      UniqueDrives = new ObservableCollection<string>(drives.Select(d => d.DriveName).Distinct().OrderBy(d => d));

      TotalItems = page.TotalItems;
      StatusMessage = page.TotalItems == 0
          ? "Historie je zatím prázdná."
          : $"✅ Načteno {HistoryItems.Count} položek historie.";
      IsBusy = false;
      ShowDriveDetail = false;
   }

   [RelayCommand]
   public async Task FilterByDriveAsync(string? driveName)
   {
      if(string.IsNullOrEmpty(driveName))
      {
         await RefreshHistoryAsync();
         return;
      }

      IsBusy = true;
      SelectedDriveFilter = driveName;
      StatusMessage = $"🔍 Filtruji historii pro: {driveName}";

      var page = await _historyService.GetHistoryAsync(pageSize: 100, pageIndex: 0);

      var filtered = page.Items.Where(i => i.DriveName == driveName).ToList();

      HistoryItems = new ObservableCollection<HistoryListItem>(filtered.Select(i => new HistoryListItem
      {
         TestId = i.TestId,
         TestDate = i.TestDate,
         DriveName = i.DriveName,
         SerialNumber = i.SerialNumber,
         TestType = i.TestType,
         Grade = i.Grade.ToString(),
         Score = i.Score,
         ErrorCount = i.ErrorCount
      }));

      TotalItems = HistoryItems.Count;
      StatusMessage = $"✅ Nalezeno {TotalItems} testů pro {driveName}";
      IsBusy = false;
   }

   [RelayCommand]
   public async Task ShowDriveHistoryAsync(string driveName)
   {
      IsBusy = true;
      SelectedDriveName = driveName;
      StatusMessage = $"📊 Načítám historii disku: {driveName}";

      var history = await _historyService.GetDriveHistoryAsync(driveName);

      DriveHistory = new ObservableCollection<HistoryListItem>(history.Select(i => new HistoryListItem
      {
         TestId = i.TestId,
         TestDate = i.TestDate,
         DriveName = i.DriveName,
         SerialNumber = i.SerialNumber,
         TestType = i.TestType,
         Grade = i.Grade.ToString(),
         Score = i.Score,
         ErrorCount = i.ErrorCount
      }));

      if(DriveHistory.Count > 0)
      {
         var avgScore = DriveHistory.Average(h => h.Score);
         var avgGrade = DriveHistory.OrderByDescending(h => h.TestDate).First().Grade;
         var totalTests = DriveHistory.Count;
         var lastTest = DriveHistory.OrderByDescending(h => h.TestDate).First();

         DriveSummary = $"📊 Disk: {driveName}\n" +
                        $"Celkem testů: {totalTests}\n" +
                        $"Průměrné skóre: {avgScore:F1}\n" +
                        $"Poslední známka: {avgGrade}\n" +
                        $"Poslední test: {lastTest.TestDate:dd.MM.yyyy HH:mm}";

         CreateTrendPlot(history);
      }
      else
      {
         DriveSummary = $"Žádná historie pro disk: {driveName}";
         TrendPlotModel = null;
      }

      ShowDriveDetail = true;
      ShowDriveDetailWidth = new GridLength(400);
      StatusMessage = $"✅ Načteno {DriveHistory.Count} testů";
      IsBusy = false;
   }

   [RelayCommand]
   public void CloseDriveDetail()
   {
      ShowDriveDetail = false;
      ShowDriveDetailWidth = new GridLength(0);
      TrendPlotModel = null;
   }

   [RelayCommand]
   public async Task ExportToCsvAsync()
   {
      var saveDialog = new SaveFileDialog
      {
         Filter = "CSV soubory (*.csv)|*.csv",
         Title = "Export historie do CSV",
         FileName = $"disk-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
      };

      if(saveDialog.ShowDialog() != true)
         return;

      IsBusy = true;
      StatusMessage = "💾 Exportuji do CSV...";

      var csv = new System.Text.StringBuilder();
      csv.AppendLine("Datum;Disk;Typ testu;Známka;Skóre;Chyby");

      foreach(var item in HistoryItems)
      {
         csv.AppendLine($"{item.TestDate:dd.MM.yyyy HH:mm};{item.DriveName};{item.TestType};{item.Grade};{item.Score:F1};{item.ErrorCount}");
      }

      await File.WriteAllTextAsync(saveDialog.FileName, csv.ToString(), System.Text.Encoding.UTF8);

      StatusMessage = $"✅ Exportováno do: {saveDialog.FileName}";
      IsBusy = false;
   }

   private void CreateTrendPlot(System.Collections.Generic.List<TestHistoryItem> history)
   {
      var model = new PlotModel
      {
         Title = "Trend skóre v čase",
         Background = OxyColors.White,
         PlotAreaBorderColor = OxyColors.Gray
      };

      model.Axes.Add(new DateTimeAxis
      {
         Position = AxisPosition.Bottom,
         Title = "Datum",
         StringFormat = "dd.MM.yyyy",
         MajorGridlineStyle = LineStyle.Dot,
         MajorGridlineColor = OxyColors.LightGray
      });

      model.Axes.Add(new LinearAxis
      {
         Position = AxisPosition.Left,
         Title = "Skóre",
         Minimum = 0,
         Maximum = 100,
         MajorGridlineStyle = LineStyle.Dot,
         MajorGridlineColor = OxyColors.LightGray
      });

      var scoreSeries = new LineSeries
      {
         Title = "Skóre",
         Color = OxyColors.Green,
         StrokeThickness = 2,
         MarkerType = MarkerType.Circle,
         MarkerSize = 4,
         MarkerFill = OxyColors.Green
      };

      var sortedHistory = history.OrderBy(h => h.TestDate).ToList();
      foreach(var item in sortedHistory)
      {
         scoreSeries.Points.Add(new DataPoint(DateTimeAxis.ToDouble(item.TestDate), item.Score));
      }

      model.Series.Add(scoreSeries);
      TrendPlotModel = model;
   }

   public override async Task InitializeAsync()
   {
      await RefreshHistoryAsync();
   }
}