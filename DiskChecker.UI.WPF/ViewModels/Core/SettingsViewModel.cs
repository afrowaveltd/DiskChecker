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
using System.Text.Json;

namespace DiskChecker.UI.WPF.ViewModels;
public partial class SettingsViewModel : ViewModelBase
{
   private readonly HistoryService _historyService;
   private readonly DatabaseMaintenanceService _maintenanceService;
   private readonly DiskHistoryArchiveService _archiveService;
   private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

   private ObservableCollection<DriveCompareItem> _drives = [];
   private DriveCompareItem? _selectedDrive;
   private int _totalHistoryItems;
   private string _archivePath = string.Empty;
   private bool _isDryRun = true;

   public SettingsViewModel(
       HistoryService historyService,
       DatabaseMaintenanceService maintenanceService,
       DiskHistoryArchiveService archiveService)
   {
       _historyService = historyService;
       _maintenanceService = maintenanceService;
       _archiveService = archiveService;
       StatusMessage = "Správa databáze připravena.";
   }

   public ObservableCollection<DriveCompareItem> Drives
   {
      get => _drives;
      private set => SetProperty(ref _drives, value);
   }

   public DriveCompareItem? SelectedDrive
   {
      get => _selectedDrive;
      set => SetProperty(ref _selectedDrive, value);
   }

   public int TotalHistoryItems
   {
      get => _totalHistoryItems;
      private set => SetProperty(ref _totalHistoryItems, value);
   }

   public string ArchivePath
   {
      get => _archivePath;
      set => SetProperty(ref _archivePath, value);
   }

   public bool IsDryRun
   {
      get => _isDryRun;
      set => SetProperty(ref _isDryRun, value);
   }

   public override async Task InitializeAsync()
   {
      await RefreshDatabaseOverviewAsync();
   }

   /// <summary>
   /// Refreshes DB overview metrics and drive list.
   /// </summary>
   [RelayCommand]
   public async Task RefreshDatabaseOverviewAsync()
   {
      IsBusy = true;
      var paged = await _historyService.GetHistoryAsync(pageSize: 1, pageIndex: 0);
      TotalHistoryItems = paged.TotalItems;

      var drives = await _historyService.GetDrivesWithTestsAsync();
      Drives = new ObservableCollection<DriveCompareItem>(drives.OrderByDescending(d => d.LastTestDate));
      StatusMessage = $"Načteno {Drives.Count} disků s historií a {TotalHistoryItems} aktivních testů.";
      IsBusy = false;
   }

   /// <summary>
   /// Deletes invalid and duplicate tests from DB.
   /// </summary>
   [RelayCommand]
   public async Task CleanupDatabaseAsync()
   {
      IsBusy = true;

      if(IsDryRun)
      {
         var preview = await _maintenanceService.PreviewCleanupAsync();
         StatusMessage = $"Dry-run: neplatné {preview.InvalidTests}, duplicity {preview.DuplicateTests}, celkem ke smazání {preview.TotalToRemove}.";
         IsBusy = false;
         return;
      }

      int invalid = await _maintenanceService.DeleteInvalidAndIncompleteTestsAsync();
      int duplicates = await _maintenanceService.RemoveDuplicateTestsAsync();
      await RefreshDatabaseOverviewAsync();
      StatusMessage = $"Údržba DB dokončena. Smazáno neplatných: {invalid}, duplicit: {duplicates}.";
      IsBusy = false;
   }

   /// <summary>
   /// Archives selected drive history to ZIP while preserving latest measurement.
   /// </summary>
   [RelayCommand]
   public async Task ArchiveSelectedDriveAsync()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "Vyberte disk k archivaci.";
         return;
      }

      IsBusy = true;
      string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DiskCheckerArchives");
      Directory.CreateDirectory(folder);
      string safeName = string.IsNullOrWhiteSpace(SelectedDrive.DriveName)
          ? SelectedDrive.DriveId.ToString()
          : SelectedDrive.DriveName.Replace(' ', '_');
      string targetZip = Path.Combine(folder, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

      int archivedCount = await _archiveService.ArchiveDriveHistoryAsync(SelectedDrive.DriveId, targetZip);
      ArchivePath = targetZip;
      await RefreshDatabaseOverviewAsync();
      StatusMessage = archivedCount == 0
          ? "Archivace neproběhla (disk má pouze poslední měření)."
          : $"Archivováno {archivedCount} testů do {targetZip}";
      IsBusy = false;
   }

   /// <summary>
   /// Imports archive from ZIP into active DB.
   /// </summary>
   [RelayCommand]
   public async Task ImportArchiveAsync()
   {
      string path = ArchivePath;
      if(string.IsNullOrWhiteSpace(path))
      {
         var dialog = new OpenFileDialog
         {
            Filter = "DiskChecker archiv (*.zip)|*.zip",
            Title = "Vyberte archiv historie disku"
         };

         if(dialog.ShowDialog() != true)
         {
            return;
         }

         path = dialog.FileName;
         ArchivePath = path;
      }

       IsBusy = true;
       int imported = await _archiveService.ImportDriveHistoryArchiveAsync(path);
       await RefreshDatabaseOverviewAsync();
       StatusMessage = $"Import dokončen. Načteno {imported} testů ze souboru {Path.GetFileName(path)}.";
       IsBusy = false;
    }

    [RelayCommand]
    public async Task GenerateDecommissionReportAsync()
    {
       if(SelectedDrive == null)
       {
          StatusMessage = "Vyberte disk pro vygenerování pohřebního listu.";
          return;
       }

       IsBusy = true;
       StatusMessage = "📄 Generuji pohřební list...";

       try
       {
          var history = await _historyService.GetDriveHistoryAsync(SelectedDrive.DriveName);
          if(history.Count == 0)
          {
             StatusMessage = "Disk nemá žádnou historii testů.";
             IsBusy = false;
             return;
          }

          var report = GenerateDecommissionReport(SelectedDrive, history);
          
          var saveDialog = new SaveFileDialog
          {
             Filter = "JSON soubory (*.json)|*.json|Textové soubory (*.txt)|*.txt",
             Title = "Uložit pohřební list",
             FileName = $"decommission_{SelectedDrive.DriveName?.Replace(' ', '_') ?? SelectedDrive.DriveId.ToString()}_{DateTime.Now:yyyyMMdd}.json"
          };

          if(saveDialog.ShowDialog() == true)
          {
             var json = JsonSerializer.Serialize(report, s_jsonOptions);
             await File.WriteAllTextAsync(saveDialog.FileName, json);
             StatusMessage = $"✅ Pohřební list uložen: {saveDialog.FileName}";
          }
          else
          {
             StatusMessage = "Pohřební list nebyl uložen.";
          }
       }
       catch(Exception ex)
       {
          StatusMessage = $"❌ Chyba při generování: {ex.Message}";
       }
       finally
       {
          IsBusy = false;
       }
    }

    private DecommissionReport GenerateDecommissionReport(DriveCompareItem drive, List<TestHistoryItem> history)
    {
       var firstTest = history.OrderBy(h => h.TestDate).First();
       var lastTest = history.OrderByDescending(h => h.TestDate).First();
       var avgScore = history.Average(h => h.Score);
       var avgErrors = history.Average(h => h.ErrorCount);
       var totalTests = history.Count;

       var gradeDistribution = history.GroupBy(h => h.Grade)
           .ToDictionary(g => g.Key.ToString(), g => g.Count());

       return new DecommissionReport
       {
          GeneratedAt = DateTime.UtcNow,
          DriveName = drive.DriveName,
          SerialNumber = drive.SerialNumber,
          Model = drive.Model,
          TotalTests = totalTests,
          FirstTestDate = firstTest.TestDate,
          LastTestDate = lastTest.TestDate,
          AverageScore = avgScore,
          AverageErrors = avgErrors,
          GradeDistribution = gradeDistribution,
          TestHistory = history.Select(h => new TestSummary
          {
             Date = h.TestDate,
             Type = h.TestType,
             Grade = h.Grade.ToString(),
             Score = h.Score
          }).ToList(),
          Recommendation = GenerateRecommendation(avgScore, avgErrors, totalTests)
       };
    }

    private string GenerateRecommendation(double avgScore, double avgErrors, int totalTests)
    {
       if(avgScore >= 90 && avgErrors == 0)
          return "Disk byl ve vynikajícím stavu. Vhodné pro další použití v méně kritických aplikacích.";

       if(avgScore >= 80 && avgErrors < 5)
          return "Disk byl v dobrém stavu s drobnými problémy. Doporučena opatrnost při opětovném nasazení.";

       if(avgScore >= 70)
          return "Disk vykazoval známky degradace. Nedoporučuje se pro produkční použití.";

       return "Disk byl v špatném stavu. Doporučena recyklace nebo bezpečné zničení.";
    }
}

public class DecommissionReport
{
   public DateTime GeneratedAt { get; set; }
   public string? DriveName { get; set; }
   public string? SerialNumber { get; set; }
   public string? Model { get; set; }
   public int TotalTests { get; set; }
   public DateTime FirstTestDate { get; set; }
   public DateTime LastTestDate { get; set; }
   public double AverageScore { get; set; }
   public double AverageErrors { get; set; }
   public Dictionary<string, int> GradeDistribution { get; set; } = [];
   public List<TestSummary> TestHistory { get; set; } = [];
   public string Recommendation { get; set; } = string.Empty;
}

public class TestSummary
{
   public DateTime Date { get; set; }
   public string? Type { get; set; }
   public string Grade { get; set; } = string.Empty;
   public double Score { get; set; }
}
