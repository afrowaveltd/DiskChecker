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

public class DiskStatusCardItem
{
   public required CoreDriveInfo Drive { get; init; }
   public string DisplayName { get; init; } = string.Empty;
   public string DisplayPath { get; init; } = string.Empty;
   public string CapacityText { get; init; } = string.Empty;
   public string GradeText { get; init; } = "-";
   public string DataSourceText { get; init; } = "SMART";
   public string TemperatureText { get; init; } = "N/A";
   public string PowerOnHoursText { get; init; } = "N/A";
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
            await LoadSystemDiskQuickReportAsync();
            return;
         }

         LoadingState = "Načítám seznam disků...";
         StatusMessage = "📂 Načítám seznam disků...";
         IReadOnlyList<CoreDriveInfo> driveList = await _diskCheckerService.ListDrivesAsync();
         Drives = driveList.ToList();

         LoadingState = "Načítám SMART data disků...";
         await LoadDiskCardsAsync();

         LoadingState = "Načítám rychlý report systémového disku...";
         await LoadSystemDiskQuickReportAsync();

         LoadingState = "Načítám poslední testy...";
         await LoadRecentHistoryAsync();

         _cachedDrives = Drives.ToList();
         _cachedDiskCards = new ObservableCollection<DiskStatusCardItem>(DiskCards);
         _cachedRecentTests = new ObservableCollection<HomeRecentTestItem>(RecentTests);
         _cacheTimestampUtc = DateTime.UtcNow;

         LoadingState = "Hotovo";
         StatusMessage = $"✅ Nalezeno {Drives.Count} disků";
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

   private async Task LoadSystemDiskQuickReportAsync()
   {
      var systemDisk = await TryGetSystemDiskAsync();
      if(systemDisk == null)
      {
         SystemDiskSummary = "Nelze mapovat systémový disk pro rychlý SMART report.";
         return;
      }

      var smartData = await GetSmartSnapshotWithTimeoutAsync(systemDisk, TimeSpan.FromSeconds(6));
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

   private async Task LoadRecentHistoryAsync()
   {
      var history = await _historyService.GetForCompareAsync(6);
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

   private async Task<CoreDriveInfo?> TryGetSystemDiskAsync()
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

      var driveLetter = systemRoot[0];
      var command = $"(Get-Partition -DriveLetter '{driveLetter}' | Get-Disk).Number";
      var psi = new ProcessStartInfo
      {
         FileName = "powershell.exe",
         Arguments = $"-NoProfile -Command \"{command}\"",
         RedirectStandardOutput = true,
         UseShellExecute = false,
         CreateNoWindow = true
      };

      using var process = Process.Start(psi);
      if(process == null)
      {
         return Drives.FirstOrDefault();
      }

      var output = await process.StandardOutput.ReadToEndAsync();
      await process.WaitForExitAsync();

      if(!int.TryParse(output.Trim(), out var systemDiskNumber))
      {
         return Drives.FirstOrDefault();
      }

      return Drives.FirstOrDefault(d => TryExtractDiskNumber(d.Path, out var number) && number == systemDiskNumber)
         ?? Drives.FirstOrDefault();
   }

   private static bool TryExtractDiskNumber(string path, out int diskNumber)
   {
      diskNumber = -1;
      if(string.IsNullOrWhiteSpace(path))
      {
         return false;
      }

      var digits = new string(path.Where(char.IsDigit).ToArray());
      return int.TryParse(digits, out diskNumber);
   }

   private async Task LoadDiskCardsAsync()
   {
      var cards = new List<DiskStatusCardItem>(Drives.Count);
      var total = Drives.Count;
      var index = 0;

      foreach(var drive in Drives)
      {
         index++;
         LoadingState = $"SMART načítání disku {index}/{total}: {drive.Name}";

         var smartData = await GetSmartSnapshotWithTimeoutAsync(drive, TimeSpan.FromSeconds(6));
         var quality = smartData == null ? null : _qualityCalculator.CalculateQuality(smartData);

         cards.Add(new DiskStatusCardItem
         {
            Drive = drive,
            DisplayName = drive.Name,
            DisplayPath = FormatDrivePath(drive.Path),
            CapacityText = FormatCapacity(drive.TotalSize),
            GradeText = quality?.Grade.ToString() ?? "OS",
            DataSourceText = smartData == null ? "OS fallback" : "SMART",
            TemperatureText = smartData?.Temperature > 0 ? $"{smartData.Temperature:F1} °C" : "N/A",
            PowerOnHoursText = smartData?.PowerOnHours > 0 ? smartData.PowerOnHours.ToString() : "N/A"
         });
      }

      DiskCards = new ObservableCollection<DiskStatusCardItem>(cards);
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
}
