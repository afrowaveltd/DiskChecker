using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

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

   /// <summary>
   /// Initializes a new instance of the <see cref="ReportViewModel"/> class.
   /// </summary>
   public ReportViewModel(HistoryService historyService)
   {
      _historyService = historyService;
      StatusMessage = "Přehled reportů připraven.";
   }

   /// <summary>
   /// Načte souhrn reportů z historie testů.
   /// </summary>
   [RelayCommand]
   public async Task RefreshReportAsync()
   {
      IsBusy = true;
      StatusMessage = "📄 Načítám souhrn reportů...";

      var history = await _historyService.GetForCompareAsync(20);

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
          .Take(10)
          .Select(i => new ReportSummaryItem
          {
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
   }

   /// <summary>
   /// Initializes the view model asynchronously.
   /// </summary>
   public override async Task InitializeAsync()
   {
      await RefreshReportAsync();
   }
}

/// <summary>
/// ViewModel pro historii testů.
/// </summary>
