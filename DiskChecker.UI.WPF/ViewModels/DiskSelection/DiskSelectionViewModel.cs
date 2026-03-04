using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.WPF.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DiskChecker.UI.WPF.ViewModels;

public class HomeRecentTestItem
{
   public DateTime TestDate { get; set; }
   public string DriveName { get; set; } = string.Empty;
   public string TestType { get; set; } = string.Empty;
   public string Grade { get; set; } = string.Empty;
   public double Score { get; set; }
}

public class DiskStatusCardItem : INotifyPropertyChanged
{
   private bool _isSelected;

   /// <summary>
   /// Základní informace o disku.
   /// </summary>
   public required CoreDriveInfo Drive { get; init; }

   /// <summary>
   /// Zobrazované jméno disku.
   /// </summary>
   public string DisplayName { get; init; } = string.Empty;

   /// <summary>
   /// Zobrazovaná cesta disku.
   /// </summary>
   public string DisplayPath { get; init; } = string.Empty;

   /// <summary>
   /// Kapacita disku.
   /// </summary>
   public string CapacityText { get; init; } = string.Empty;

   /// <summary>
   /// Známka kvality SMART.
   /// </summary>
   public string GradeText { get; init; } = "-";

   /// <summary>
   /// Zdroj SMART dat.
   /// </summary>
   public string DataSourceText { get; init; } = "SMART";

   /// <summary>
   /// Aktuální teplota.
   /// </summary>
   public string TemperatureText { get; init; } = "N/A";

   /// <summary>
   /// Počet provozních hodin.
   /// </summary>
   public string PowerOnHoursText { get; init; } = "N/A";

   /// <summary>
   /// Zda se jedná o systémový disk.
   /// </summary>
   public bool IsSystemDisk { get; init; }

   /// <summary>
   /// Label pro systémový disk.
   /// </summary>
   public string IsSystemDiskLabel => "⚠️ Systémový disk";

   /// <summary>
   /// Zda je disk vybraný.
   /// </summary>
   public bool IsSelected
   {
      get => _isSelected;
      set
      {
         if(_isSelected != value)
         {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
         }
      }
   }

   /// <summary>
   /// Implementace INotifyPropertyChanged.
   /// </summary>
   public event PropertyChangedEventHandler? PropertyChanged;

   /// <summary>
   /// Vyvolá PropertyChanged event.
   /// </summary>
   protected virtual void OnPropertyChanged(string propertyName)
   {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
   }
}

/// <summary>
/// ViewModel pro výběr disku.
/// </summary>
public partial class DiskSelectionViewModel : ViewModelBase
{
   private static List<CoreDriveInfo>? _cachedDrives;
   private static ObservableCollection<DiskStatusCardItem>? _cachedDiskCards;
   private static ObservableCollection<HomeRecentTestItem>? _cachedRecentTests;
   private static DateTime _cacheTimestampUtc;

   private readonly DiskCheckerService _diskCheckerService;
   private readonly SmartCheckService _smartCheckService;
   private readonly IQualityCalculator _qualityCalculator;
   private readonly HistoryService _historyService;
   private readonly DiskHistoryArchiveService _archiveService;
   private readonly INavigationService _navigationService;

   [ObservableProperty]
   private List<CoreDriveInfo> drives = [];

   [ObservableProperty]
   private CoreDriveInfo? selectedDrive;

   private string _systemDiskName = "Systémový disk: nenačten";
   private string _systemDiskGrade = "-";
   private string _systemDiskTemperature = "-";
   private string _systemDiskSummary = "Rychlý SMART report bude načten po startu.";
   private string _loadingState = "Připraveno";
   private ObservableCollection<HomeRecentTestItem> _recentTests = [];

   public string SystemDiskName
   {
      get => _systemDiskName;
      private set => SetProperty(ref _systemDiskName, value);
   }

   public string SystemDiskGrade
   {
      get => _systemDiskGrade;
      private set => SetProperty(ref _systemDiskGrade, value);
   }

   public string SystemDiskTemperature
   {
      get => _systemDiskTemperature;
      private set => SetProperty(ref _systemDiskTemperature, value);
   }

   public string SystemDiskSummary
   {
      get => _systemDiskSummary;
      private set => SetProperty(ref _systemDiskSummary, value);
   }

   public ObservableCollection<HomeRecentTestItem> RecentTests
   {
      get => _recentTests;
      private set => SetProperty(ref _recentTests, value);
   }

   public string LoadingState
   {
      get => _loadingState;
      private set => SetProperty(ref _loadingState, value);
   }

   private bool _isSurfaceActionActive;
   private bool _isSmartActionActive;
   private bool _isDetailActionActive;

   public bool IsSurfaceActionActive
   {
      get => _isSurfaceActionActive;
      private set => SetProperty(ref _isSurfaceActionActive, value);
   }

   public bool IsSmartActionActive
   {
      get => _isSmartActionActive;
      private set => SetProperty(ref _isSmartActionActive, value);
   }

   public bool IsDetailActionActive
   {
      get => _isDetailActionActive;
      private set => SetProperty(ref _isDetailActionActive, value);
   }

   private ObservableCollection<DiskStatusCardItem> _diskCards = [];

   public ObservableCollection<DiskStatusCardItem> DiskCards
   {
      get => _diskCards;
      private set => SetProperty(ref _diskCards, value);
   }

   private DiskStatusCardItem? _selectedDiskCard;

   public DiskStatusCardItem? SelectedDiskCard
   {
      get => _selectedDiskCard;
      set
      {
         if(SetProperty(ref _selectedDiskCard, value))
         {
            SelectedDrive = value?.Drive;
         }
      }
   }

   /// <summary>
   /// Initializes a new instance of the <see cref="DiskSelectionViewModel"/> class.
   /// </summary>
   public DiskSelectionViewModel(
      DiskCheckerService diskCheckerService,
      SmartCheckService smartCheckService,
      IQualityCalculator qualityCalculator,
      HistoryService historyService,
      DiskHistoryArchiveService archiveService,
      INavigationService navigationService)
   {
      _diskCheckerService = diskCheckerService;
      _smartCheckService = smartCheckService;
      _qualityCalculator = qualityCalculator;
      _historyService = historyService;
      _archiveService = archiveService;
      _navigationService = navigationService;
   }

   /// <summary>
   /// Initializes the view model by loading available drives.
   /// </summary>
   public override async Task InitializeAsync()
   {
      await LoadDisksAsync(forceRefresh: false);
   }

   [RelayCommand]
   public async Task ReloadDisksAsync()
   {
      await LoadDisksAsync(forceRefresh: true);
   }

   private async Task LoadDisksAsync(bool forceRefresh)
   {
      IsBusy = true;

      try
      {
         if(!forceRefresh && _cachedDrives != null && _cachedDiskCards != null && _cachedRecentTests != null)
         {
            Drives = _cachedDrives.ToList();
            DiskCards = new ObservableCollection<DiskStatusCardItem>(_cachedDiskCards);
            RecentTests = new ObservableCollection<HomeRecentTestItem>(_cachedRecentTests);
            LoadingState = $"Načteno z cache ({_cacheTimestampUtc:HH:mm:ss})";
            StatusMessage = $"✅ Nalezeno {Drives.Count} disků (cache)";
            return;
         }

         LoadingState = "Načítám seznam disků...";
         StatusMessage = "📂 Načítám seznam disků...";
         IReadOnlyList<CoreDriveInfo> driveList = await _diskCheckerService.ListDrivesAsync();
         Drives = driveList.ToList();

         // Vytvořit prázdné karty hned
         DiskCards = new ObservableCollection<DiskStatusCardItem>(
            Drives.Select(d => new DiskStatusCardItem
            {
               Drive = d,
               DisplayName = d.Name,
               DisplayPath = FormatDrivePath(d.Path),
               CapacityText = FormatCapacity(d.TotalSize),
               GradeText = "...",
               DataSourceText = "Načítání...",
               TemperatureText = "-",
               PowerOnHoursText = "-",
               IsSystemDisk = false
            }));

         LoadingState = "Načítám SMART data disků...";
         StatusMessage = "⚙️ Načítám SMART data...";
         
         // Aktualizuj SMART data asynchronně na pozadí
         _ = UpdateDiskCardsWithSmartDataAsync();

         LoadingState = "Hotovo";
         StatusMessage = $"✅ Nalezeno {Drives.Count} disků";

         // Paralelně načítej systemový disk report a historii na pozadí bez čekání
         _ = Task.Run(async () =>
         {
            try
            {
               await LoadSystemDiskQuickReportAsync();
            }
            catch(Exception ex)
            {
               System.Diagnostics.Debug.WriteLine($"LoadSystemDiskQuickReportAsync bg error: {ex.Message}");
            }
         });

         _ = Task.Run(async () =>
         {
            try
            {
               await LoadRecentHistoryAsync();
            }
            catch(Exception ex)
            {
               System.Diagnostics.Debug.WriteLine($"LoadRecentHistoryAsync bg error: {ex.Message}");
            }
         });

         // Cache uložíme AŽ PO NAČTENÍ SMART DAT
         // Čekáme trochu aby se SMART data načetla
         _ = Task.Run(async () =>
         {
            await Task.Delay(4000); // 4 sekundy by mělo stačit
            _cachedDrives = Drives.ToList();
            _cachedDiskCards = new ObservableCollection<DiskStatusCardItem>(DiskCards);
            _cachedRecentTests = new ObservableCollection<HomeRecentTestItem>(RecentTests);
            _cacheTimestampUtc = DateTime.UtcNow;
            System.Diagnostics.Debug.WriteLine("Cache saved after SMART data loaded");
         });

      }
      catch(InvalidOperationException ex)
      {
         LoadingState = "Chyba";
         StatusMessage = $"❌ Chyba: {ex.Message}";
      }
      catch(Win32Exception ex)
      {
         LoadingState = "Chyba prostředí";
         StatusMessage = $"❌ Chyba prostředí: {ex.Message}";
      }
      finally
      {
         IsBusy = false;
      }
   }

   private async Task UpdateDiskCardsWithSmartDataAsync()
   {
      try
      {
         System.Diagnostics.Debug.WriteLine("=== UpdateDiskCardsWithSmartDataAsync START ===");
         await LoadDiskCardsAsync();
         System.Diagnostics.Debug.WriteLine("=== UpdateDiskCardsWithSmartDataAsync SUCCESS ===");
      }
      catch(Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"=== UpdateDiskCardsWithSmartDataAsync ERROR ===");
         System.Diagnostics.Debug.WriteLine($"Exception: {ex.GetType().Name}");
         System.Diagnostics.Debug.WriteLine($"Message: {ex.Message}");
         System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
      }
   }

   private async Task LoadDiskCardsAsync()
   {
      System.Diagnostics.Debug.WriteLine("LoadDiskCardsAsync: Starting");

      try
      {
         // Paralelně načítej SMART data pro všechny disky
         var smartDataTasks = Drives.Select(async drive =>
         {
            try
            {
               System.Diagnostics.Debug.WriteLine($"LoadDiskCardsAsync: Starting SMART load for {drive.Name}");
               var smartData = await GetSmartSnapshotWithTimeoutAsync(drive, TimeSpan.FromSeconds(1.5));
               var quality = smartData == null ? null : _qualityCalculator.CalculateQuality(smartData);
               return (drive, smartData, quality, success: true);
            }
            catch(Exception ex)
            {
               System.Diagnostics.Debug.WriteLine($"LoadDiskCardsAsync: SMART load error for {drive.Name}: {ex.Message}");
               return (drive, smartData: null as SmartaData, quality: null as QualityRating, success: false);
            }
         }).ToList();

         System.Diagnostics.Debug.WriteLine("LoadDiskCardsAsync: Waiting for SMART tasks");
         var allSmartTask = Task.WhenAll(smartDataTasks);
         var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3));
         
         var completedTask = await Task.WhenAny(allSmartTask, timeoutTask);

         List<(CoreDriveInfo, SmartaData?, QualityRating?, bool)> results;
         if(completedTask == allSmartTask && allSmartTask.IsCompletedSuccessfully)
         {
            results = allSmartTask.Result.ToList();
            System.Diagnostics.Debug.WriteLine("LoadDiskCardsAsync: SMART tasks completed successfully");
         }
         else
         {
            System.Diagnostics.Debug.WriteLine("LoadDiskCardsAsync: SMART tasks timeout - returning partial results");
            results = smartDataTasks
               .Where(t => t.IsCompleted && !t.IsFaulted)
               .Select(t => t.Result)
               .ToList();
         }

         // Zjisti systémový disk
         System.Diagnostics.Debug.WriteLine("LoadDiskCardsAsync: Getting system disk");
         var systemDisk = await Task.Run(async () =>
         {
            try
            {
               using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
               return await TryGetSystemDiskAsync(cts.Token);
            }
            catch(Exception ex)
            {
               System.Diagnostics.Debug.WriteLine($"LoadDiskCardsAsync: System disk error: {ex.Message}");
               return null;
            }
         });

         System.Diagnostics.Debug.WriteLine($"LoadDiskCardsAsync: Updating {results.Count} cards");
         
         // AKTUALIZUJ EXISTUJÍCÍ KARTY místo vytváření nových!
         System.Windows.Application.Current?.Dispatcher.Invoke(() =>
         {
            System.Diagnostics.Debug.WriteLine("LoadDiskCardsAsync: Updating cards on UI thread");
            
            foreach(var (drive, smartData, quality, success) in results)
            {
               // Najdi existující kartu pro tento disk
               var card = DiskCards.FirstOrDefault(c => c.Drive.Path == drive.Path);
               if(card != null)
               {
                  // Aktualizuj data v existující kartě
                  var isSystemDisk = systemDisk != null && 
                                    (string.Equals(systemDisk.Name, drive.Name, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(systemDisk.Path, drive.Path, StringComparison.OrdinalIgnoreCase));

                  // POZOR: DiskStatusCardItem properties jsou init-only!
                  // Musíme vytvořit novou kartu a nahradit ji
                  var index = DiskCards.IndexOf(card);
                  DiskCards[index] = new DiskStatusCardItem
                  {
                     Drive = drive,
                     DisplayName = drive.Name,
                     DisplayPath = FormatDrivePath(drive.Path),
                     CapacityText = FormatCapacity(drive.TotalSize),
                     GradeText = quality?.Grade.ToString() ?? "OS",
                     DataSourceText = smartData == null ? "OS fallback" : "SMART",
                     TemperatureText = smartData?.Temperature > 0 ? $"{smartData.Temperature:F1} °C" : "N/A",
                     PowerOnHoursText = smartData?.PowerOnHours > 0 ? smartData.PowerOnHours.ToString() : "N/A",
                     IsSystemDisk = isSystemDisk
                  };
               }
            }
            
            System.Diagnostics.Debug.WriteLine("LoadDiskCardsAsync: Cards updated successfully");
         });
      }
      catch(Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"LoadDiskCardsAsync: CRITICAL ERROR - {ex.GetType().Name}: {ex.Message}");
         System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
      }

      System.Diagnostics.Debug.WriteLine("LoadDiskCardsAsync: Finished");
   }

   private async Task<SmartaData?> GetSmartSnapshotWithTimeoutAsync(CoreDriveInfo drive, TimeSpan timeout)
   {
      using var cts = new CancellationTokenSource(timeout);
      try
      {
         return await _smartCheckService.GetSmartaDataWithRetryAsync(drive, 1, cts.Token);
      }
      catch(OperationCanceledException)
      {
         return null;
      }
      catch(InvalidOperationException)
      {
         return null;
      }
   }

   private async Task<CoreDriveInfo?> TryGetSystemDiskAsync(CancellationToken cancellationToken = default)
   {
      if(!OperatingSystem.IsWindows())
      {
         return Drives.FirstOrDefault();
      }

      var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
      if(string.IsNullOrWhiteSpace(systemRoot))
      {
         return Drives.FirstOrDefault();
      }

      if(!systemRoot.EndsWith(":\\", StringComparison.OrdinalIgnoreCase))
      {
         return Drives.FirstOrDefault();
      }

      try
      {
         var driveLetter = systemRoot[0];
         var command = $"(Get-Partition -DriveLetter '{driveLetter}' | Get-Disk).Number";
         var psi = new ProcessStartInfo
         {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -Command \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
         };

         using var process = Process.Start(psi);
         if(process == null)
         {
            return Drives.FirstOrDefault();
         }

         var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
         var waitTask = process.WaitForExitAsync(cancellationToken);

         await Task.WhenAll(outputTask, waitTask);

         var output = outputTask.Result;
         if(!int.TryParse(output.Trim(), out var systemDiskNumber))
         {
            return Drives.FirstOrDefault();
         }

         return Drives.FirstOrDefault(d => TryExtractDiskNumber(d.Path, out var number) && number == systemDiskNumber)
            ?? Drives.FirstOrDefault();
      }
      catch(OperationCanceledException)
      {
         System.Diagnostics.Debug.WriteLine("TryGetSystemDiskAsync timeout");
         return Drives.FirstOrDefault();
      }
      catch(Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"TryGetSystemDiskAsync error: {ex.Message}");
         return Drives.FirstOrDefault();
      }
   }

   private static bool TryExtractDiskNumber(string path, out int number)
   {
      number = -1;
      if(path.Contains("PhysicalDrive", StringComparison.OrdinalIgnoreCase))
      {
         var numStr = new string(path.Where(char.IsDigit).ToArray());
         if(int.TryParse(numStr, out number))
         {
            return true;
         }
      }
      return false;
   }

   private static string FormatDrivePath(string path)
   {
      if(string.IsNullOrWhiteSpace(path))
      {
         return "N/A";
      }

      return path
         .Replace("\\\\.\\", string.Empty, StringComparison.OrdinalIgnoreCase)
         .Replace("/", string.Empty, StringComparison.OrdinalIgnoreCase);
   }

   private static string FormatCapacity(long bytes)
   {
      if(bytes <= 0)
      {
         return "N/A";
      }

      const double gb = 1024d * 1024d * 1024d;
      const double tb = gb * 1024d;
      return bytes >= tb
         ? $"{bytes / tb:F2} TB"
         : $"{bytes / gb:F2} GB";
   }

   /// <summary>
   /// Navigates to surface test with selected drive.
   /// </summary>
   [RelayCommand]
   public void NavigateToSurfaceTest()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "❌ Prosím vyber disk!";
         return;
      }

      SetActiveAction(surface: true);
      StatusMessage = $"🔄 Přecházím na test povrchu: {SelectedDrive.Name}";
      _navigationService.NavigateTo<SurfaceTestViewModel>(SelectedDrive);
   }

   /// <summary>
   /// Navigates to SMART check with selected drive.
   /// </summary>
   [RelayCommand]
   public void NavigateToSmartCheck()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "❌ Prosím vyber disk!";
         return;
      }

      SetActiveAction(smart: true);
      StatusMessage = $"🔄 Přecházím na SMART check: {SelectedDrive.Name}";
      _navigationService.NavigateTo<SmartCheckViewModel>(SelectedDrive);
   }

   /// <summary>
   /// Navigates to disk detail view (implemented as SMART detail).
   /// </summary>
   [RelayCommand]
   public void NavigateToDiskDetail()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "❌ Prosím vyber disk!";
         return;
      }

      SetActiveAction(detail: true);
      StatusMessage = $"🔎 Otevírám detail disku: {SelectedDrive.Name}";
      _navigationService.NavigateTo<SmartCheckViewModel>(SelectedDrive);
   }

   private void SetActiveAction(bool surface = false, bool smart = false, bool detail = false)
   {
      IsSurfaceActionActive = surface;
      IsSmartActionActive = smart;
      IsDetailActionActive = detail;
   }

   /// <summary>
   /// Archives history of the selected tile disk and keeps latest measurement in DB.
   /// </summary>
   [RelayCommand]
   public async Task ArchiveDiskFromTileAsync(DiskStatusCardItem? card)
   {
      if(card == null)
      {
         StatusMessage = "Nelze archivovat: disk není vybrán.";
         return;
      }

      var drivesWithTests = await _historyService.GetDrivesWithTestsAsync();
      var match = drivesWithTests.FirstOrDefault(d =>
         string.Equals(d.DriveName, card.Drive.Name, StringComparison.OrdinalIgnoreCase)
         || string.Equals(d.SerialNumber, card.Drive.Path, StringComparison.OrdinalIgnoreCase)
         || string.Equals(d.Model, card.Drive.Name, StringComparison.OrdinalIgnoreCase));

      if(match == null)
      {
         StatusMessage = "Pro vybraný disk nebyla nalezena archivovatelná historie.";
         return;
      }

      var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DiskCheckerArchives");
      Directory.CreateDirectory(folder);
      var safeName = string.IsNullOrWhiteSpace(match.DriveName) ? match.DriveId.ToString() : match.DriveName.Replace(' ', '_');
      var targetZip = Path.Combine(folder, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

      var archived = await _archiveService.ArchiveDriveHistoryAsync(match.DriveId, targetZip);
      StatusMessage = archived == 0
         ? "Archivace přeskočena (disk má pouze poslední měření)."
         : $"Archivováno {archived} testů disku {match.DriveName} do {targetZip}";
   }

   private async Task LoadSystemDiskQuickReportAsync()
   {
      try
      {
         // Timeout 2 sekundy na systémový disk lookup
         using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
         var systemDisk = await TryGetSystemDiskAsync(cts.Token);
         
         if(systemDisk == null)
         {
            SystemDiskSummary = "Nelze mapovat systémový disk.";
            return;
         }

         // Timeout 2 sekundy na SMART data
         var smartData = await GetSmartSnapshotWithTimeoutAsync(systemDisk, TimeSpan.FromSeconds(2));
         if(smartData == null)
         {
            SystemDiskName = $"Systémový disk: {systemDisk.Name}";
            SystemDiskGrade = "OS";
            SystemDiskTemperature = "N/A";
            SystemDiskSummary = "SMART data nejsou dostupná.";
            return;
         }

         var quality = _qualityCalculator.CalculateQuality(smartData);
         SystemDiskName = $"Systémový disk: {systemDisk.Name}";
         SystemDiskGrade = quality.Grade.ToString();
         SystemDiskTemperature = smartData.Temperature > 0 ? $"{smartData.Temperature:F1} °C" : "N/A";
         SystemDiskSummary = quality.Warnings.Count == 0
            ? "Bez kritických varování"
            : string.Join(" | ", quality.Warnings.Take(2));
      }
      catch(OperationCanceledException)
      {
         System.Diagnostics.Debug.WriteLine("LoadSystemDiskQuickReportAsync timeout");
         SystemDiskSummary = "Časový limit při načítání reportu.";
      }
      catch(Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"LoadSystemDiskQuickReportAsync error: {ex.Message}");
         SystemDiskSummary = "Chyba při načítání SMART reportu.";
      }
   }

   private async Task LoadRecentHistoryAsync()
   {
      try
      {
         // Pokud je databáze pomalá, skip to
         var historyTask = _historyService.GetForCompareAsync(6);
         var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2));
         
         var completedTask = await Task.WhenAny(historyTask, timeoutTask);
         
         if(completedTask == historyTask && historyTask.IsCompletedSuccessfully)
         {
            var history = historyTask.Result;
            RecentTests = new ObservableCollection<HomeRecentTestItem>(history
               .OrderByDescending(t => t.TestDate)
               .Select(t => new HomeRecentTestItem
               {
                  TestDate = t.TestDate,
                  DriveName = t.DriveName,
                  TestType = t.TestType,
                  Grade = t.Grade.ToString(),
                  Score = t.Score
               }));
         }
         else
         {
            // Timeout - databáze je pomalá
            System.Diagnostics.Debug.WriteLine("LoadRecentHistoryAsync timeout - databáze je pomalá");
            RecentTests = [];
         }
      }
      catch(Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"LoadRecentHistoryAsync error: {ex.Message}");
         RecentTests = [];
      }
   }
}
