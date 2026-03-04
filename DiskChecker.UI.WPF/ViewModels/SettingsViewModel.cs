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
public partial class SettingsViewModel : ViewModelBase
{
   private readonly HistoryService _historyService;
   private readonly DatabaseMaintenanceService _maintenanceService;
   private readonly DiskHistoryArchiveService _archiveService;

   private ObservableCollection<DriveCompareItem> _drives = [];
   private DriveCompareItem? _selectedDrive;
   private int _totalHistoryItems;
   private string _archivePath = string.Empty;
   private bool _isDryRun = true;

   /// <summary>
   /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
   /// </summary>
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
}
