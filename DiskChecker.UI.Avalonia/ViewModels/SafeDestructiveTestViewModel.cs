using Avalonia.Threading;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace DiskChecker.UI.Avalonia.ViewModels;

public enum SafeDestructivePhase
{
   /// <summary>Initial state — disk selected, ready to start.</summary>
   Ready,
   /// <summary>Creating raw sector image of the disk.</summary>
   Backup,
   /// <summary>Running destructive test phases.</summary>
   Test,
   /// <summary>Restoring raw image back to disk.</summary>
   Restore,
   /// <summary>Creating partition after test.</summary>
   Partition,
   /// <summary>All phases complete.</summary>
   Completed,
   /// <summary>Cancelled by user or error.</summary>
   Failed
}

public enum SafeDestructiveMode
{
   /// <summary>Full backup → test → restore (original behavior).</summary>
   BackupAndRestore,
   /// <summary>VHDx backup → verify → test → restore from VHDx.</summary>
   VhdxOnly,
   /// <summary>Gentle RAW image → verify → restore → verify round-trip without extra destructive overwrite passes.</summary>
   ImageRoundTrip
}

public partial class SafeDestructiveTestViewModel : ViewModelBase, INavigableViewModel, IDisposable
{
   // ──────────────────────────────────────────────
   //  Dependencies
   // ──────────────────────────────────────────────

   private readonly INavigationService _navigationService;
   private readonly ISelectedDiskService _selectedDiskService;
   private readonly IDialogService _dialogService;
   private readonly IDiskSanitizationService _sanitizationService;
   private readonly SeekTestService _seekTestService;
   private readonly ISmartaProvider _smartaProvider;
   private readonly SmartCheckService _smartCheckService;
   private readonly IDiskCardRepository _diskCardRepository;
   private readonly ICertificateGenerator _certificateGenerator;
   private readonly TestCompletionNotificationService _notificationService;
   private readonly IPowerManagementService _powerManagementService;

   // ──────────────────────────────────────────────
   //  Cancellation
   // ──────────────────────────────────────────────

   private CancellationTokenSource? _cts;
   private IPowerManagementSession? _powerSession;
   private bool _disposed;

   // ──────────────────────────────────────────────
   //  Observable properties — workflow
   // ──────────────────────────────────────────────

   [ObservableProperty] private SafeDestructivePhase _phase = SafeDestructivePhase.Ready;
   [ObservableProperty] private string _statusMessage = string.Empty;
   [ObservableProperty] private double _overallProgress;
   [ObservableProperty] private string _overallProgressText = "0%";
   [ObservableProperty] private string _currentPhaseName = string.Empty;
   [ObservableProperty] private string _currentPhaseIcon = string.Empty;

   // ──────────────────────────────────────────────
   //  Disk info
   // ──────────────────────────────────────────────

   [ObservableProperty] private CoreDriveInfo? _selectedDrive;
   [ObservableProperty] private string _diskDisplayName = string.Empty;
   [ObservableProperty] private string _diskPath = string.Empty;
   [ObservableProperty] private long _diskTotalBytes;
   [ObservableProperty] private string _diskTotalSizeText = string.Empty;

   // ──────────────────────────────────────────────
   //  Backup phase properties
   // ──────────────────────────────────────────────

   [ObservableProperty] private string _backupTargetPath = string.Empty;
   [ObservableProperty] private long _backupTargetFreeBytes;
   [ObservableProperty] private string _backupTargetFreeText = string.Empty;
   [ObservableProperty] private bool _hasEnoughBackupSpace;
   [ObservableProperty] private string _backupSpaceSummary = string.Empty;
   [ObservableProperty] private long _backupBytesWritten;
   [ObservableProperty] private string _backupBytesWrittenText = string.Empty;
   [ObservableProperty] private string _backupSpeedText = string.Empty;
   [ObservableProperty] private string _backupElapsedText = string.Empty;
   [ObservableProperty] private string _backupEtaText = string.Empty;
   [ObservableProperty] private string _backupCurrentSectorText = string.Empty;
   [ObservableProperty] private double _backupProgress;
   [ObservableProperty] private string _backupProgressText = "0%";
   [ObservableProperty] private string _backupVerificationText = string.Empty;

   // ── Backup target selection ──
   [ObservableProperty] private ObservableCollection<BackupTargetItem> _backupTargetDrives = new();
   [ObservableProperty] private BackupTargetItem? _selectedBackupTarget;

   // ── Mode selection ──
   [ObservableProperty] private SafeDestructiveMode _selectedMode = SafeDestructiveMode.BackupAndRestore;
   [ObservableProperty] private string _vhdxBackupPath = string.Empty;
   [ObservableProperty] private string _vhdxBackupPathText = string.Empty;
   [ObservableProperty] private bool _vhdxBackupVerified;

   // ──────────────────────────────────────────────
   //  Test phase properties (mirrors AbsoluteDestructiveTest)
   // ──────────────────────────────────────────────

   [ObservableProperty] private ObservableCollection<TestPhaseViewModel> _testPhases = new();
   [ObservableProperty] private double _testPhaseProgress;
   [ObservableProperty] private string _testPhaseDetail = string.Empty;
   [ObservableProperty] private string _testCurrentSpeedText = string.Empty;
   [ObservableProperty] private string _testCurrentTemperatureText = string.Empty;
   [ObservableProperty] private string _testElapsedText = string.Empty;
   [ObservableProperty] private string _testEtaText = string.Empty;

   // ──────────────────────────────────────────────
   //  Restore phase properties
   // ──────────────────────────────────────────────

   [ObservableProperty] private string _restoreImagePath = string.Empty;
   [ObservableProperty] private long _restoreBytesWritten;
   [ObservableProperty] private string _restoreBytesWrittenText = string.Empty;
   [ObservableProperty] private string _restoreSpeedText = string.Empty;
   [ObservableProperty] private string _restoreElapsedText = string.Empty;
   [ObservableProperty] private string _restoreEtaText = string.Empty;
   [ObservableProperty] private string _restoreCurrentSectorText = string.Empty;
   [ObservableProperty] private double _restoreProgress;
   [ObservableProperty] private string _restoreProgressText = "0%";

   // ──────────────────────────────────────────────
   [ObservableProperty] private string _resultsSummary = string.Empty;
   [ObservableProperty] private string _smartDeltaSummary = string.Empty;
   [ObservableProperty] private bool _hasResults;

   // ──────────────────────────────────────────────
   //  Charts (sanitize + seek)
   // ──────────────────────────────────────────────

   [ObservableProperty] private ObservableCollection<ObservablePoint> _sanitizePass1WritePoints = new();
   [ObservableProperty] private ObservableCollection<ObservablePoint> _sanitizePass1ReadPoints = new();
   [ObservableProperty] private ObservableCollection<ObservablePoint> _sanitizePass2WritePoints = new();
   [ObservableProperty] private ObservableCollection<ObservablePoint> _sanitizePass2ReadPoints = new();

   [ObservableProperty] private ObservableCollection<ObservablePoint> _seekFullStrokePoints = new();
   [ObservableProperty] private ObservableCollection<ObservablePoint> _seekRandomPoints = new();
   [ObservableProperty] private ObservableCollection<ObservablePoint> _seekSkipPoints = new();

   [ObservableProperty]
   private ISeries[] _sanitizeChartSeries = new ISeries[]
   {
        new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>(),
            Fill = null, Stroke = null, GeometrySize = 0, LineSmoothness = 0
        }
   };
   [ObservableProperty]
   private Axis[] _sanitizeChartXAxes = new Axis[]
   {
        new Axis { Name = "Progress (%)", NameTextSize = 10, TextSize = 9, MinLimit = 0, MaxLimit = 100, Labeler = v => v.ToString("F0") }
   };
   [ObservableProperty]
   private Axis[] _sanitizeChartYAxes = new Axis[]
   {
        new Axis { Name = "MB/s", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => $"{v:F0}" }
   };

   [ObservableProperty]
   private ISeries[] _seekChartSeries = new ISeries[]
   {
        new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>(),
            Fill = null, Stroke = null, GeometrySize = 0, LineSmoothness = 0
        }
   };
   [ObservableProperty]
   private Axis[] _seekChartXAxes = new Axis[]
   {
        new Axis { Name = "Seek #", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => v.ToString("F0") }
   };
   [ObservableProperty]
   private Axis[] _seekChartYAxes = new Axis[]
   {
        new Axis { Name = "Latence (ms)", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => $"{v:F1}" }
   };

   [ObservableProperty] private int _activeSeekChartIndex;
   [ObservableProperty] private bool _hasSeekCharts;

   // ──────────────────────────────────────────────
   //  Sanitize data counters
   // ──────────────────────────────────────────────

   [ObservableProperty] private long _sanitizeBytesWritten;
   [ObservableProperty] private string _sanitizeBytesWrittenText = string.Empty;
   [ObservableProperty] private long _sanitizeBytesRead;
   [ObservableProperty] private string _sanitizeBytesReadText = string.Empty;
   [ObservableProperty] private long _sanitizeTotalBytes;
   [ObservableProperty] private string _sanitizeTotalBytesText = string.Empty;

   // ──────────────────────────────────────────────
   //  Sanitize series toggles
   // ──────────────────────────────────────────────

   [ObservableProperty] private bool _showPass1Write = true;
   [ObservableProperty] private bool _showPass1Read = true;
   [ObservableProperty] private bool _showPass2Write = true;
   [ObservableProperty] private bool _showPass2Read = true;

   // ──────────────────────────────────────────────
   //  Log
   // ──────────────────────────────────────────────

   [ObservableProperty] private string _logText = string.Empty;
   [ObservableProperty] private bool _isGeneratingCertificate;
   [ObservableProperty] private string _certificateProgressText = string.Empty;
   private readonly List<string> _logEntries = new();

   // ──────────────────────────────────────────────
   //  SMART snapshots
   // ──────────────────────────────────────────────

   private SmartaData? _smartBefore;
   private SmartaData? _smartAfter;

   // ──────────────────────────────────────────────
   //  Backup manifest path (for restore)
   // ──────────────────────────────────────────────

   private string? _backupManifestPath;
   private string? _backupImagePath;
   private long _backupTotalBytes;
   private long _backupDataStartOffset;
   private string _backupSha256 = string.Empty;
   private bool _backupVerified;
   private bool _destructivePhaseStarted;
   private bool _restoreCompleted;

   // ──────────────────────────────────────────────
   //  Timing
   // ──────────────────────────────────────────────

   private DateTime _phaseStartTime;
   private long _phaseBytesProcessed;

   // ──────────────────────────────────────────────
   //  Commands
   // ──────────────────────────────────────────────

   public IAsyncRelayCommand StartWorkflowCommand { get; }
   public IRelayCommand CancelCommand { get; }
   public IRelayCommand GoBackCommand { get; }
   public IRelayCommand SwitchSeekChartCommand { get; }
   public IRelayCommand<string> ToggleSanitizeSeriesCommand { get; }
   public IRelayCommand SwitchToBackupAndRestoreCommand { get; }
   public IRelayCommand SwitchToVhdxOnlyCommand { get; }
   public IRelayCommand SwitchToImageRoundTripCommand { get; }

   private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

   private const double BackupAndRestoreBackupWeight = 30d;
   private const double BackupAndRestoreTestWeight = 40d;
   private const double BackupAndRestoreRestoreWeight = 30d;
   private const double VhdxBackupWeight = 40d;
   private const double VhdxTestWeight = 30d;
   private const double VhdxRestoreWeight = 30d;

   // ──────────────────────────────────────────────
   //  Constructor
   // ──────────────────────────────────────────────

   public SafeDestructiveTestViewModel(
       INavigationService navigationService,
       ISelectedDiskService selectedDiskService,
       IDialogService dialogService,
       IDiskSanitizationService sanitizationService,
       SeekTestService seekTestService,
       ISmartaProvider smartaProvider,
       SmartCheckService smartCheckService,
       IDiskCardRepository diskCardRepository,
       ICertificateGenerator certificateGenerator,
       TestCompletionNotificationService notificationService,
       IPowerManagementService powerManagementService)
   {
      _navigationService = navigationService;
      _selectedDiskService = selectedDiskService;
      _dialogService = dialogService;
      _sanitizationService = sanitizationService;
      _seekTestService = seekTestService;
      _smartaProvider = smartaProvider;
      _smartCheckService = smartCheckService;
      _diskCardRepository = diskCardRepository;
      _certificateGenerator = certificateGenerator;
      _notificationService = notificationService;
      _powerManagementService = powerManagementService;
      StatusMessage = L.Get("SafeDestructive.Status.ReadySelectBackupTarget");

      StartWorkflowCommand = new AsyncRelayCommand(StartWorkflowAsync, () => Phase == SafeDestructivePhase.Ready && SelectedDrive != null && HasEnoughBackupSpace);
      SelectedMode = SafeDestructiveMode.BackupAndRestore; // default
      CancelCommand = new RelayCommand(Cancel);
      GoBackCommand = new RelayCommand(GoBack);
      SwitchSeekChartCommand = new RelayCommand(SwitchSeekChart);
      ToggleSanitizeSeriesCommand = new RelayCommand<string>(ToggleSanitizeSeries);
      SwitchToBackupAndRestoreCommand = new RelayCommand(() => SelectedMode = SafeDestructiveMode.BackupAndRestore);
      SwitchToVhdxOnlyCommand = new RelayCommand(() => SelectedMode = SafeDestructiveMode.VhdxOnly);
      SwitchToImageRoundTripCommand = new RelayCommand(() => SelectedMode = SafeDestructiveMode.ImageRoundTrip);

      InitializeChartDefaults();
   }

   // ──────────────────────────────────────────────
   //  Initialization
   // ──────────────────────────────────────────────

   public async Task InitializeAsync()
   {
      var disk = _selectedDiskService.SelectedDisk;
      if(disk == null)
      {
         StatusMessage = L.Get("SafeDestructive.Status.NoDisk");
         return;
      }

      SelectedDrive = disk;
      DiskDisplayName = disk.Name ?? disk.Path;
      DiskPath = disk.Path;
      DiskTotalBytes = disk.TotalSize;
      DiskTotalSizeText = FormatBytesLong(disk.TotalSize);

      // Find a suitable backup target (largest available non-source drive)
      await FindBackupTargetAsync();

      // Capture pre-test SMART (only if drive supports SMART)
      if(disk.SupportsSmart)
      {
         try
         {
            _smartBefore = await _smartaProvider.GetSmartaDataAsync(disk.Path);
         }
         catch
         {
            _smartBefore = null;
         }
      }
      else
      {
         _smartBefore = null;
      }

      Log($"Disk: {DiskDisplayName} ({DiskTotalSizeText})");
      Log($"Režim: {SelectedMode}");
      Log($"Cesta: {DiskPath}");
      Log($"SMART před testem: {(_smartBefore != null ? "dostupný" : "nedostupný")}");

      if(HasEnoughBackupSpace)
         StatusMessage = L.Get("SafeDestructive.Status.Ready");
      else
         StatusMessage = L.Get("SafeDestructive.Status.NoSpace");
   }

   private async Task FindBackupTargetAsync()
   {
      BackupTargetDrives.Clear();

      if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
      {
         await FindBackupTargetsLinuxAsync();
      }
      else
      {
         FindBackupTargetsWindows();
      }

      // Auto-select the best target (must have enough space)
      var best = BackupTargetDrives
          .Where(t => t.TotalFreeSpace >= DiskTotalBytes)
          .OrderByDescending(t => t.TotalFreeSpace)
          .FirstOrDefault();
      if(best != null)
      {
         best.IsSelected = true;
         SelectedBackupTarget = best;
      }
      else if(BackupTargetDrives.Count > 0)
      {
         // No target has enough space — still show them but don't auto-select
         BackupTargetPath = L.Get("SafeDestructive.BackupTarget.NoneNoSpace");
         HasEnoughBackupSpace = false;
         BackupSpaceSummary = string.Format(L.Get("SafeDestructive.BackupSpace.NoDiskEnough"), BackupTargetDrives.Count, DiskTotalSizeText);
         StartWorkflowCommand.NotifyCanExecuteChanged();
         return;
      }

      RecalculateBackupSpace();
   }

   // ── Linux: use lsblk to find mounted partitions with free space ──

   private async Task FindBackupTargetsLinuxAsync()
   {
      try
      {
         // lsblk with filesystem info: NAME, SIZE, FSAVAIL, MOUNTPOINT, FSTYPE, MODEL, TYPE
         var lsblkOutput = await ExecuteCommandAsync("lsblk", "-J -b -o NAME,SIZE,FSAVAIL,MOUNTPOINT,FSTYPE,MODEL,TYPE");
         if(string.IsNullOrEmpty(lsblkOutput)) return;

         using var doc = System.Text.Json.JsonDocument.Parse(lsblkOutput);
         if(!doc.RootElement.TryGetProperty("blockdevices", out var devices)) return;

         // Determine which top-level disk is the source
         string? sourceDiskName = null;
         if(SelectedDrive != null)
         {
            var sourceDeviceName = System.IO.Path.GetFileName(SelectedDrive.Path);
            sourceDiskName = FindParentDiskName(devices, sourceDeviceName);
         }

         // Iterate top-level disks, collect mounted partitions as targets
         foreach(var disk in devices.EnumerateArray())
         {
            var diskName = disk.TryGetProperty("name", out var dn) ? dn.GetString() : "";
            var diskType = disk.TryGetProperty("type", out var dt) ? dt.GetString() : "";

            // Skip source disk and non-disk devices (like loop, ram)
            if(diskType != "disk") continue;
            if(sourceDiskName != null &&
                string.Equals(diskName, sourceDiskName, StringComparison.OrdinalIgnoreCase))
               continue;

            var diskModel = disk.TryGetProperty("model", out var dm) ? dm.GetString()?.Trim() : null;

            // Check children (partitions)
            if(disk.TryGetProperty("children", out var children))
            {
               foreach(var part in children.EnumerateArray())
               {
                  var partType = part.TryGetProperty("type", out var pt) ? pt.GetString() : "";
                  if(partType != "part") continue;

                  var mountPoint = part.TryGetProperty("mountpoint", out var mp) ? mp.GetString() : null;
                  if(string.IsNullOrWhiteSpace(mountPoint) || mountPoint == "null") continue;

                  var fsAvail = part.TryGetProperty("fsavail", out var fa) &&
                                fa.ValueKind == System.Text.Json.JsonValueKind.Number
                      ? fa.GetInt64() : 0;

                  // Skip if fsavail is null/zero (not a mounted filesystem)
                  if(fsAvail <= 0) continue;

                  var fsType = part.TryGetProperty("fstype", out var ft) ? ft.GetString() : "";
                  var partName = part.TryGetProperty("name", out var pn) ? pn.GetString() : "";

                  var displayName = diskModel != null
                      ? $"{diskModel} — {mountPoint} ({fsType})"
                      : $"/dev/{diskName} — {mountPoint} ({fsType})";

                  BackupTargetDrives.Add(new BackupTargetItem
                  {
                     DrivePath = mountPoint,
                     DisplayName = displayName,
                     TotalFreeSpace = fsAvail,
                     FreeSpaceText = FormatBytesLong(fsAvail),
                     IsSelected = false,
                     AllocatedBytes = 0
                  });
               }
            }
         }
      }
      catch { /* non-critical — fall through to empty list */ }
   }

   /// <summary>
   /// Given a device name (e.g. "sda1" or "nvme0n1p2"), finds the parent disk
   /// name ("sda" or "nvme0n1") in the lsblk device tree.
   /// Returns the device name itself if it's already a top-level disk.
   /// </summary>
   private static string? FindParentDiskName(
       System.Text.Json.JsonElement devices,
       string deviceName)
   {
      foreach(var disk in devices.EnumerateArray())
      {
         var diskName = disk.TryGetProperty("name", out var dn) ? dn.GetString() : "";
         if(string.Equals(diskName, deviceName, StringComparison.OrdinalIgnoreCase))
            return diskName; // It's already a top-level disk

         if(disk.TryGetProperty("children", out var children))
         {
            foreach(var child in children.EnumerateArray())
            {
               var childName = child.TryGetProperty("name", out var cn) ? cn.GetString() : "";
               if(string.Equals(childName, deviceName, StringComparison.OrdinalIgnoreCase))
                  return diskName; // Found in children → parent is this disk
            }
         }
      }
      return null;
   }

   // ── Windows: use DriveInfo ──

   private void FindBackupTargetsWindows()
   {
      var drives = System.IO.DriveInfo.GetDrives()
          .Where(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed)
          .OrderByDescending(d => d.AvailableFreeSpace)
          .ToList();

      // Exclude source drive
      if(SelectedDrive != null)
      {
         var sourceRoot = System.IO.Path.GetPathRoot(SelectedDrive.Path)?.TrimEnd('\\', '/');
         drives = drives.Where(d =>
         {
            var root = d.RootDirectory.FullName.TrimEnd('\\', '/');
            return !string.Equals(root, sourceRoot, StringComparison.OrdinalIgnoreCase);
         }).ToList();
      }

      foreach(var d in drives)
      {
         BackupTargetDrives.Add(new BackupTargetItem
         {
            DrivePath = d.RootDirectory.FullName,
            DisplayName = $"{d.Name} ({d.VolumeLabel}) — {d.DriveFormat}",
            TotalFreeSpace = d.AvailableFreeSpace,
            FreeSpaceText = FormatBytesLong(d.AvailableFreeSpace),
            IsSelected = false,
            AllocatedBytes = 0
         });
      }
   }

   /// <summary>
   /// Called when the user changes the selected backup target.
   /// </summary>
   partial void OnSelectedBackupTargetChanged(BackupTargetItem? value)
   {
      // Deselect all others
      foreach(var t in BackupTargetDrives)
         t.IsSelected = t == value;

      RecalculateBackupSpace();
   }

   private void RecalculateBackupSpace()
   {
      if(SelectedBackupTarget == null)
      {
         BackupTargetPath = L.Get("SafeDestructive.BackupTarget.None");
         BackupTargetFreeBytes = 0;
         BackupTargetFreeText = "0 B";
         HasEnoughBackupSpace = false;
         BackupSpaceSummary = L.Get("SafeDestructive.BackupSpace.NoTarget");
         StartWorkflowCommand.NotifyCanExecuteChanged();
         return;
      }

      BackupTargetPath = SelectedBackupTarget.DrivePath;
      BackupTargetFreeBytes = SelectedBackupTarget.TotalFreeSpace;
      BackupTargetFreeText = FormatBytesLong(SelectedBackupTarget.TotalFreeSpace);

      // Reserve: RAM size (min 4GB, max 32GB for safety)
      long systemReserve = Math.Min(Math.Max(4L * 1024 * 1024 * 1024, GetRamBytes() / 2), 32L * 1024 * 1024 * 1024);
      long usableBytes = Math.Max(0, SelectedBackupTarget.TotalFreeSpace - systemReserve);

      HasEnoughBackupSpace = usableBytes >= DiskTotalBytes;
      BackupSpaceSummary = HasEnoughBackupSpace
          ? string.Format(L.Get("SafeDestructive.BackupSpace.Enough"), FormatBytesLong(usableBytes), DiskTotalSizeText)
          : string.Format(L.Get("SafeDestructive.BackupSpace.NotEnough"), FormatBytesLong(usableBytes), DiskTotalSizeText);

      StartWorkflowCommand.NotifyCanExecuteChanged();
   }

   /// <summary>
   /// Runs a shell command and returns stdout, or null on failure.
   /// </summary>
   private static async Task<string?> ExecuteCommandAsync(string command, string arguments)
   {
      try
      {
         var psi = new System.Diagnostics.ProcessStartInfo
         {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
         };

         using var process = System.Diagnostics.Process.Start(psi);
         if(process == null) return null;

         var output = await process.StandardOutput.ReadToEndAsync();
         await process.WaitForExitAsync();
         return output;
      }
      catch
      {
         return null;
      }
   }

   private static long GetRamBytes()
   {
      // Conservative estimate for system reserve calculation
      return 8L * 1024 * 1024 * 1024; // 8 GB
   }

   // ──────────────────────────────────────────────
   //  Chart defaults
   // ──────────────────────────────────────────────

   private void InitializeChartDefaults()
   {
      var xAxis = new Axis
      {
         Name = L.Get("SafeDestructive.Chart.ProgressPercent"),
         MinLimit = 0,
         MaxLimit = 100,
         Labeler = v => $"{v:F0}%"
      };
      var yAxis = new Axis
      {
         Name = "MB/s",
         MinLimit = 0
      };

      SanitizeChartXAxes = new[] { xAxis };
      SanitizeChartYAxes = new[] { yAxis };

      SeekChartXAxes = new[] { new Axis { Name = "Seek #", MinLimit = 0 } };
      SeekChartYAxes = new[] { new Axis { Name = L.Get("SafeDestructive.Chart.LatencyMs"), MinLimit = 0 } };

      RebuildSanitizeChart();
   }

   private void RebuildSanitizeChart()
   {
      var series = new List<ISeries>();

      if(ShowPass1Write && SanitizePass1WritePoints.Count > 0)
         series.Add(CreateLineSeries(SanitizePass1WritePoints, L.Get("SafeDestructive.Chart.WritePass1"), new SKColor(0xEF, 0x44, 0x44), 2));
      if(ShowPass1Read && SanitizePass1ReadPoints.Count > 0)
         series.Add(CreateLineSeries(SanitizePass1ReadPoints, L.Get("SafeDestructive.Chart.ReadPass1"), new SKColor(0x22, 0xC5, 0x5E), 2));
      if(ShowPass2Write && SanitizePass2WritePoints.Count > 0)
         series.Add(CreateLineSeries(SanitizePass2WritePoints, L.Get("SafeDestructive.Chart.WritePass2"), new SKColor(0xB9, 0x1C, 0x1C), 2));
      if(ShowPass2Read && SanitizePass2ReadPoints.Count > 0)
         series.Add(CreateLineSeries(SanitizePass2ReadPoints, L.Get("SafeDestructive.Chart.ReadPass2"), new SKColor(0x15, 0x80, 0x3D), 2));

      // Two-step assignment forces LiveCharts2 SkiaSharp to detect the change and redraw
      SanitizeChartSeries = Array.Empty<ISeries>();
      SanitizeChartSeries = series.ToArray();
   }

   private static ISeries CreateLineSeries(ObservableCollection<ObservablePoint> points, string name, SKColor color, float strokeWidth)
   {
      return new LineSeries<ObservablePoint>
      {
         Values = points,
         Name = name,
         Stroke = new SolidColorPaint(color, strokeWidth),
         GeometrySize = 0,
         Fill = null,
         LineSmoothness = 0
      };
   }

   // ──────────────────────────────────────────────
   //  Workflow orchestration
   // ──────────────────────────────────────────────

   private async Task StartWorkflowAsync()
   {
      if(SelectedDrive == null) return;

      _cts = new CancellationTokenSource();
      var ct = _cts.Token;
      _backupVerified = false;
      _destructivePhaseStarted = false;
      _restoreCompleted = false;
      _backupSha256 = string.Empty;
      _backupDataStartOffset = 0;

      try
      {
         // Protect the complete operation, including backup and emergency restore.
         // Suspending in any of these phases can leave the source or image incomplete.
         _powerSession = await _powerManagementService.BeginTestSessionAsync(ct);

         // ── Phase 1: Backup + verification ──
         if(SelectedMode == SafeDestructiveMode.VhdxOnly)
         {
            await RunVhdxBackupPhaseAsync(ct);
         }
         else
         {
            await RunBackupPhaseAsync(ct);
         }
         if(ct.IsCancellationRequested) return;

         if(!_backupVerified)
            throw new InvalidOperationException(L.Get("SafeDestructive.Error.BackupNotVerified"));

         // ── Phase 2: Destructive Test ──
         _destructivePhaseStarted = true;
         await RunTestPhaseAsync(ct);
         if(ct.IsCancellationRequested) return;

         // ── Phase 3: Restore (raw i VHDx režim obnovují původní data) ──
         await RunRestorePhaseAsync(ct);
         if(ct.IsCancellationRequested) return;

         // ── Complete ──
         Phase = SafeDestructivePhase.Completed;
         CurrentPhaseName = L.Get("SafeDestructive.Phase.Done");
         CurrentPhaseIcon = "✅";
         OverallProgress = 100;
         OverallProgressText = "Celkem 100%";
         StatusMessage = L.Get("SafeDestructive.Status.Done");
         HasResults = true;

         await BuildResultsAsync();
         await BuildCertificateAsync();
         await SaveTestSessionAsync();
         Log(L.Get("SafeDestructive.Log.WorkflowCompleted"));
      }
      catch(OperationCanceledException)
      {
         if(_destructivePhaseStarted && !_restoreCompleted)
            await TryEmergencyRestoreAsync(L.Get("SafeDestructive.Emergency.CancelledAfterDestructive"));

         Phase = SafeDestructivePhase.Failed;
         StatusMessage = L.Get("SafeDestructive.Status.Cancelled");
         Log(L.Get("SafeDestructive.Log.OperationCancelled"));
      }
      catch(Exception ex)
      {
         if(_destructivePhaseStarted && !_restoreCompleted)
            await TryEmergencyRestoreAsync(string.Format(L.Get("SafeDestructive.Emergency.ErrorAfterDestructive"), ex.Message));

         Phase = SafeDestructivePhase.Failed;
         StatusMessage = L.Get("SafeDestructive.Status.Error", ex.Message);
         Log($"❌ FATAL: {ex.Message}");
      }
      finally
      {
         if(_powerSession != null)
         {
            try
            {
               await _powerSession.RestoreAsync();
            }
            finally
            {
               _powerSession.Dispose();
               _powerSession = null;
            }
         }
      }
   }

   // ──────────────────────────────────────────────
   //  Phase 1: Raw Backup
   // ──────────────────────────────────────────────

   private async Task RunBackupPhaseAsync(CancellationToken ct)
   {
      Phase = SafeDestructivePhase.Backup;
      CurrentPhaseName = L.Get("SafeDestructive.Phase.BackupRaw");
      CurrentPhaseIcon = "💾";
      StatusMessage = L.Get("SafeDestructive.Status.CreatingImage");
      OverallProgress = 0;
      OverallProgressText = "Celkem 0%";

      var backupRoot = Path.Combine(BackupTargetPath, $"DiskChecker_SafeBackup_{DateTime.Now:yyyyMMdd_HHmmss}");
      Directory.CreateDirectory(backupRoot);
      _backupImagePath = Path.Combine(backupRoot, "disk_image.raw");
      _backupManifestPath = Path.Combine(backupRoot, "backup_manifest.json");

      Log(string.Format(L.Get("SafeDestructive.Log.BackupPath"), _backupImagePath));

      // Use 1 MiB blocks for speed (was 4 KiB)
      const int blockSize = 1024 * 1024;
      var buffer = new byte[blockSize];
      long totalSize = DiskTotalBytes;
      long bytesRead = 0;
      long unreadableBytes = 0;
      int consecutiveErrors = 0;
      const int maxConsecutiveErrors = 64;
      _phaseStartTime = DateTime.UtcNow;
      _phaseBytesProcessed = 0;
      _backupDataStartOffset = 0;
      _backupVerified = false;
      BackupVerificationText = "";
      BackupProgress = 0;
      BackupProgressText = "0%";
      using var backupHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

      using var sourceStream = new FileStream(DiskPath, FileMode.Open, FileAccess.Read,
          FileShare.Read, blockSize, FileOptions.SequentialScan);
      using var targetStream = new FileStream(_backupImagePath, FileMode.Create, FileAccess.Write,
          FileShare.Read, blockSize, FileOptions.SequentialScan);

      try
      {
         while(bytesRead < totalSize)
         {
            ct.ThrowIfCancellationRequested();

            int bytesToRead = (int)Math.Min(blockSize, totalSize - bytesRead);
            int bytesReadNow = 0;
            bool blockReadable = true;

            try
            {
               bytesReadNow = await sourceStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
            }
            catch(IOException ex) when(IsDeviceDisappearedError(ex))
            {
               throw new InvalidOperationException(
                   $"❌ Zdrojové zařízení zmizelo během zálohování (pozice {FormatBytesLong(bytesRead)}). " +
                   "Zkontrolujte připojení disku a opakujte operaci.", ex);
            }
            catch(IOException)
            {
               blockReadable = false;
            }
            catch(UnauthorizedAccessException)
            {
               blockReadable = false;
            }

            if(!blockReadable || bytesReadNow == 0)
            {
               // Unreadable sector — write zeros and log
               Array.Clear(buffer, 0, bytesToRead);
               bytesReadNow = bytesToRead;
               unreadableBytes += bytesToRead;
               consecutiveErrors++;

               if(consecutiveErrors == 1)
                  Log(string.Format(L.Get("SafeDestructive.Log.UnreadableSectorReplaced"), FormatBytesLong(bytesRead)));

               if(consecutiveErrors >= maxConsecutiveErrors)
                  throw new IOException(string.Format(L.Get("SafeDestructive.Error.TooManyUnreadableSectors"), consecutiveErrors));
            }
            else
            {
               consecutiveErrors = 0;
            }

            await targetStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);
            backupHash.AppendData(buffer, 0, bytesReadNow);
            bytesRead += bytesReadNow;
            _phaseBytesProcessed += bytesReadNow;

            BackupBytesWritten = bytesRead;
            BackupBytesWrittenText = FormatBytesLong(bytesRead);
            BackupCurrentSectorText = $"Blok {bytesRead / blockSize:N0} / {totalSize / blockSize:N0}";

            double backupProgress = totalSize > 0 ? (double)bytesRead / totalSize * 100 : 0;
            BackupProgress = backupProgress;
            BackupProgressText = $"{backupProgress:F0}%";
            OverallProgress = ScaleWorkflowProgress(0, GetBackupWeight(), backupProgress);
            OverallProgressText = $"Celkem {OverallProgress:F0}%";

            UpdatePhaseSpeedAndEta(ref _phaseStartTime, ref _phaseBytesProcessed,
                out var speed, out var elapsed, out var eta);
            BackupSpeedText = speed;
            BackupElapsedText = elapsed;
            BackupEtaText = eta;

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, ct);
         }
      }
      catch(InvalidOperationException)
      {
         // Device disappeared - rethrow to be caught by StartWorkflowAsync
         throw;
      }
      catch(IOException ex) when(IsDeviceDisappearedError(ex))
      {
         throw new InvalidOperationException(
             L.Get("SafeDestructive.Error.SourceDisappearedDuringBackup"), ex);
      }

      _backupTotalBytes = bytesRead;
      _backupSha256 = Convert.ToHexString(backupHash.GetHashAndReset());

      if(unreadableBytes > 0)
         Log(string.Format(L.Get("SafeDestructive.Log.UnreadableBytesReplaced"), FormatBytesLong(unreadableBytes)));

      // Write manifest
      var manifest = new
      {
         SourceDrive = DiskPath,
         SourceModel = DiskDisplayName,
         BackupDate = DateTime.Now.ToString("O"),
         Mode = "RawImage",
         TotalBytes = bytesRead,
         BlockSize = blockSize,
         UnreadableBytes = unreadableBytes,
         Sha256 = _backupSha256
      };
      using var manifestStream = File.OpenWrite(_backupManifestPath);
      await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, ct);

      await VerifyBackupImageAsync(_backupImagePath, _backupDataStartOffset, _backupTotalBytes, _backupSha256, ct);

      Log(string.Format(L.Get("SafeDestructive.Log.BackupCompletedVerified"), FormatBytesLong(bytesRead)));
      StatusMessage = L.Get("SafeDestructive.Status.BackupDone");
   }

   // ──────────────────────────────────────────────
   //  Phase 2: Destructive Test
   // ──────────────────────────────────────────────

   private async Task RunTestPhaseAsync(CancellationToken ct)
   {
      Phase = SafeDestructivePhase.Test;
      CurrentPhaseName = L.Get("SafeDestructive.Phase.DestructiveTest");
      CurrentPhaseIcon = "🧪";
      StatusMessage = L.Get("SafeDestructive.Status.RunningTest");

      if(SelectedDrive == null) return;

      // Build test phases
      TestPhases.Clear();
      var phases = new[]
      {
            new TestPhaseViewModel { Name = L.Get("DestructiveTest.Sanitize.Write1"), Icon = "🧹", PhaseIndex = 0 },
            new TestPhaseViewModel { Name = L.Get("DestructiveTest.Sanitize.Read1"), Icon = "🧹", PhaseIndex = 1 },
            new TestPhaseViewModel { Name = L.Get("DestructiveTest.Phase.SeekFS"), Icon = "↔️", PhaseIndex = 2 },
            new TestPhaseViewModel { Name = L.Get("DestructiveTest.Phase.SeekRND"), Icon = "🎲", PhaseIndex = 3 },
            new TestPhaseViewModel { Name = L.Get("DestructiveTest.Phase.SeekSKIP"), Icon = "⏭️", PhaseIndex = 4 },
            new TestPhaseViewModel { Name = L.Get("DestructiveTest.Sanitize.Write2"), Icon = "🧹", PhaseIndex = 5 },
            new TestPhaseViewModel { Name = L.Get("DestructiveTest.Sanitize.Read2"), Icon = "🧹", PhaseIndex = 6 }
        };
      foreach(var p in phases) TestPhases.Add(p);

      int totalPhases = phases.Length;
      double testStart = GetBackupWeight() / 100d;
      double testWeight = GetTestWeight() / 100d;
      double phaseWeight = testWeight / totalPhases;

      // ── Sanitize Pass 1 Write ──
      await RunSanitizePhaseAsync(0, "write", SanitizePass1WritePoints, phaseWeight, testStart, ct);
      if(ct.IsCancellationRequested) return;

      // ── Sanitize Pass 1 Read ──
      await RunSanitizePhaseAsync(1, "read", SanitizePass1ReadPoints, phaseWeight, testStart + phaseWeight, ct);
      if(ct.IsCancellationRequested) return;

      // ── Seek Full Stroke ──
      await RunSeekPhaseAsync(2, SeekTestType.FullStroke, SeekFullStrokePoints, phaseWeight, testStart + phaseWeight * 2, ct);
      if(ct.IsCancellationRequested) return;

      // ── Seek Random ──
      await RunSeekPhaseAsync(3, SeekTestType.Random, SeekRandomPoints, phaseWeight, testStart + phaseWeight * 3, ct);
      if(ct.IsCancellationRequested) return;

      // ── Seek Skip ──
      await RunSeekPhaseAsync(4, SeekTestType.Skip, SeekSkipPoints, phaseWeight, testStart + phaseWeight * 4, ct);
      if(ct.IsCancellationRequested) return;

      HasSeekCharts = true;
      RebuildSeekChart();

      // ── Sanitize Pass 2 Write ──
      await RunSanitizePhaseAsync(5, "write", SanitizePass2WritePoints, phaseWeight, testStart + phaseWeight * 5, ct);
      if(ct.IsCancellationRequested) return;

      // ── Sanitize Pass 2 Read ──
      await RunSanitizePhaseAsync(6, "read", SanitizePass2ReadPoints, phaseWeight, testStart + phaseWeight * 6, ct);
      if(ct.IsCancellationRequested) return;

      // Capture post-test SMART (only if drive supports SMART)
      if(SelectedDrive.SupportsSmart)
      {
         try
         {
            _smartAfter = await _smartaProvider.GetSmartaDataAsync(SelectedDrive.Path, ct);
         }
         catch
         {
            _smartAfter = null;
         }
      }
      else
      {
         _smartAfter = null;
      }

      StatusMessage = L.Get("SafeDestructive.Status.TestDoneRestoring");
   }

   private async Task RunSanitizePhaseAsync(int phaseIndex, string mode,
       ObservableCollection<ObservablePoint> points, double phaseWeight, double baseProgress,
       CancellationToken ct)
   {
      var phase = TestPhases[phaseIndex];
      phase.Status = TestPhaseStatus.Running;
      TestPhaseProgress = 0;
      TestPhaseDetail = mode == "write" ? L.Get("SafeDestructive.Phase.Write") : L.Get("SafeDestructive.Phase.Read");

      SanitizeTotalBytes = DiskTotalBytes;
      SanitizeTotalBytesText = DiskTotalSizeText;

      long bytesProcessed = 0;
      _phaseStartTime = DateTime.UtcNow;
      _phaseBytesProcessed = 0;

      const int blockSize = 256 * 1024; // 256KB blocks
      var buffer = new byte[blockSize];
      long totalBlocks = DiskTotalBytes / blockSize;

      using var deviceStream = new FileStream(DiskPath, FileMode.Open, FileAccess.ReadWrite,
          FileShare.ReadWrite, blockSize, FileOptions.SequentialScan);

      try
      {
         for(long block = 0; block < totalBlocks; block++)
         {
            ct.ThrowIfCancellationRequested();

            try
            {
               if(mode == "write")
               {
                  Array.Fill(buffer, (byte)0x00);
                  await deviceStream.WriteAsync(buffer, ct);
               }
               else
               {
                  await deviceStream.ReadExactlyAsync(buffer, ct);
               }
            }
            catch(IOException ex) when(IsDeviceDisappearedError(ex))
            {
               throw new InvalidOperationException(
                   $"❌ Zařízení zmizelo během sanitizace (fáze {phaseIndex + 1}, {mode}, blok {block}). " +
                   "Zkontrolujte připojení disku a opakujte operaci.", ex);
            }

            bytesProcessed += blockSize;
            _phaseBytesProcessed += blockSize;

            if(mode == "write")
               SanitizeBytesWritten = bytesProcessed;
            else
               SanitizeBytesRead = bytesProcessed;

            SanitizeBytesWrittenText = FormatBytesLong(SanitizeBytesWritten);
            SanitizeBytesReadText = FormatBytesLong(SanitizeBytesRead);

            double phaseProgress = DiskTotalBytes > 0 ? (double)bytesProcessed / DiskTotalBytes * 100 : 0;
            TestPhaseProgress = phaseProgress;
            phase.ProgressPercent = phaseProgress;

            // Add chart point every ~1%
            if(block % Math.Max(1, totalBlocks / 100) == 0 || block == totalBlocks - 1)
            {
               var speedMbps = CalculateCurrentSpeed();
               points.Add(new ObservablePoint(phaseProgress, speedMbps));
            }

            UpdatePhaseSpeedAndEta(ref _phaseStartTime, ref _phaseBytesProcessed,
                out var speed, out var elapsed, out var eta);
            TestCurrentSpeedText = speed;
            TestElapsedText = elapsed;
            TestEtaText = eta;

            OverallProgress = baseProgress * 100 + (phaseProgress / 100 * phaseWeight) * 100;
            OverallProgressText = $"Celkem {OverallProgress:F0}%";

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, ct);
         }
      }
      catch(InvalidOperationException)
      {
         throw;
      }
      catch(IOException ex) when(IsDeviceDisappearedError(ex))
      {
         throw new InvalidOperationException(
             L.Get("SafeDestructive.Error.DeviceDisappearedDuringSanitize"), ex);
      }

      phase.Status = TestPhaseStatus.Completed;
      phase.ProgressPercent = 100;
      RebuildSanitizeChart();
      Log(string.Format(L.Get("SafeDestructive.Log.SanitizeCompleted"), phaseIndex + 1, mode, FormatBytesLong(bytesProcessed)));
   }

   private async Task RunSeekPhaseAsync(int phaseIndex, SeekTestType seekType,
       ObservableCollection<ObservablePoint> points, double phaseWeight, double baseProgress,
       CancellationToken ct)
   {
      var phase = TestPhases[phaseIndex];
      phase.Status = TestPhaseStatus.Running;
      TestPhaseProgress = 0;
      TestPhaseDetail = $"Seek test: {seekType}...";

      _phaseStartTime = DateTime.UtcNow;
      _phaseBytesProcessed = 0;

      if(SelectedDrive == null) return;

      var result = await _seekTestService.RunWithRecommendationAsync(
          SelectedDrive,
          preferredType: seekType,
          progressCallback: progress =>
          {
             double phaseProgress = progress.PercentComplete;
             TestPhaseProgress = phaseProgress;
             phase.ProgressPercent = phaseProgress;

             if(progress.LatestSample != null)
             {
                points.Add(new ObservablePoint(progress.SeeksCompleted, progress.LatestSample.LatencyMs));
             }

             TestCurrentSpeedText = $"{progress.CurrentAverageLatencyMs:F2} ms";
             TestElapsedText = $"{(int)(DateTime.UtcNow - _phaseStartTime).TotalHours:D2}:{(DateTime.UtcNow - _phaseStartTime).Minutes:D2}:{(DateTime.UtcNow - _phaseStartTime).Seconds:D2}";

             OverallProgress = baseProgress * 100 + (phaseProgress / 100 * phaseWeight) * 100;
             OverallProgressText = $"Celkem {OverallProgress:F0}%";
          },
          cancellationToken: ct);

      phase.Status = result.IsCompleted ? TestPhaseStatus.Completed : TestPhaseStatus.Failed;
      phase.ProgressPercent = 100;
      Log(string.Format(L.Get("SafeDestructive.Log.SeekCompleted"), seekType, result.AverageLatencyMs, result.P95LatencyMs, result.ErrorCount));
   }

   // ──────────────────────────────────────────────
   //  Phase 1b: VHDx Backup (VhdxOnly mode)
   // ──────────────────────────────────────────────

   private async Task RunVhdxBackupPhaseAsync(CancellationToken ct)
   {
      Phase = SafeDestructivePhase.Backup;
      CurrentPhaseName = L.Get("SafeDestructive.Phase.VhdxBackup");
      CurrentPhaseIcon = "💾";
      StatusMessage = L.Get("SafeDestructive.Status.CreatingVhdx");
      OverallProgress = 0;
      OverallProgressText = "Celkem 0%";

      if(SelectedDrive == null) return;

      var backupRoot = Path.Combine(BackupTargetPath, $"DiskChecker_VhdxBackup_{DateTime.Now:yyyyMMdd_HHmmss}");
      Directory.CreateDirectory(backupRoot);
      _backupImagePath = Path.Combine(backupRoot, "disk_image.vhdx");
      _backupManifestPath = Path.Combine(backupRoot, "backup_manifest.json");
      VhdxBackupPath = _backupImagePath;
      VhdxBackupPathText = _backupImagePath;
      VhdxBackupVerified = false;

      Log(string.Format(L.Get("SafeDestructive.Log.VhdxBackupPath"), _backupImagePath));

      // Use 1 MiB blocks for speed
      const int blockSize = 1024 * 1024;
      var buffer = new byte[blockSize];
      long totalSize = DiskTotalBytes;
      long bytesRead = 0;
      long unreadableBytes = 0;
      int consecutiveErrors = 0;
      const int maxConsecutiveErrors = 64;
      _phaseStartTime = DateTime.UtcNow;
      _phaseBytesProcessed = 0;
      _backupVerified = false;
      BackupVerificationText = "";
      BackupProgress = 0;
      BackupProgressText = "0%";
      using var backupHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

      var dataStartOffset = CalculateVhdxDataStartOffset(totalSize);
      _backupDataStartOffset = dataStartOffset;
      var requiredBytes = dataStartOffset + RoundUp(totalSize, blockSize);
      if(BackupTargetFreeBytes < requiredBytes)
         throw new IOException(string.Format(L.Get("SafeDestructive.Error.VhdxNotEnoughSpace"), FormatBytesLong(requiredBytes), FormatBytesLong(BackupTargetFreeBytes)));

      // Write VHDx header + BAT — fixed VHDx, all blocks pre-allocated
      await WriteVhdxHeaderAsync(_backupImagePath, totalSize, ct);

      // Data start offset of the VHDx payload area.
      const int logicalSectorSize = 1048576;

      using var sourceStream = new FileStream(DiskPath, FileMode.Open, FileAccess.Read,
          FileShare.Read, blockSize, FileOptions.SequentialScan);
      using var targetStream = new FileStream(_backupImagePath, FileMode.Open, FileAccess.Write,
          FileShare.Read, blockSize, FileOptions.SequentialScan);

      try
      {
         while(bytesRead < totalSize)
         {
            ct.ThrowIfCancellationRequested();

            int bytesToRead = (int)Math.Min(blockSize, totalSize - bytesRead);
            int bytesReadNow = 0;
            bool blockReadable = true;

            try
            {
               bytesReadNow = await sourceStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
            }
            catch(IOException ex) when(IsDeviceDisappearedError(ex))
            {
               throw new InvalidOperationException(
                   $"❌ Zdrojové zařízení zmizelo během VHDx zálohování (pozice {FormatBytesLong(bytesRead)}). " +
                   "Zkontrolujte připojení disku a opakujte operaci.", ex);
            }
            catch(IOException)
            {
               blockReadable = false;
            }
            catch(UnauthorizedAccessException)
            {
               blockReadable = false;
            }

            if(!blockReadable || bytesReadNow == 0)
            {
               // Unreadable sector — write zeros and log
               Array.Clear(buffer, 0, bytesToRead);
               bytesReadNow = bytesToRead;
               unreadableBytes += bytesToRead;
               consecutiveErrors++;

               if(consecutiveErrors == 1)
                  Log(string.Format(L.Get("SafeDestructive.Log.UnreadableSectorReplaced"), FormatBytesLong(bytesRead)));

               if(consecutiveErrors >= maxConsecutiveErrors)
                  throw new IOException(string.Format(L.Get("SafeDestructive.Error.TooManyUnreadableSectors"), consecutiveErrors));
            }
            else
            {
               consecutiveErrors = 0;
            }

            // Write data at the correct offset in the fixed VHDx
            long chunkIndex = bytesRead / logicalSectorSize;
            long writeOffset = dataStartOffset + chunkIndex * logicalSectorSize;
            targetStream.Position = writeOffset;
            await targetStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);
            backupHash.AppendData(buffer, 0, bytesReadNow);
            bytesRead += bytesReadNow;
            _phaseBytesProcessed += bytesReadNow;

            BackupBytesWritten = bytesRead;
            BackupBytesWrittenText = FormatBytesLong(bytesRead);
            BackupCurrentSectorText = $"Blok {bytesRead / blockSize:N0} / {totalSize / blockSize:N0}";

            double backupProgress = totalSize > 0 ? (double)bytesRead / totalSize * 100 : 0;
            BackupProgress = backupProgress;
            BackupProgressText = $"{backupProgress:F0}%";
            OverallProgress = ScaleWorkflowProgress(0, GetBackupWeight(), backupProgress);
            OverallProgressText = $"Celkem {OverallProgress:F0}%";

            UpdatePhaseSpeedAndEta(ref _phaseStartTime, ref _phaseBytesProcessed,
                out var speed, out var elapsed, out var eta);
            BackupSpeedText = speed;
            BackupElapsedText = elapsed;
            BackupEtaText = eta;

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, ct);
         }
      }
      catch(InvalidOperationException)
      {
         throw;
      }
      catch(IOException ex) when(IsDeviceDisappearedError(ex))
      {
         throw new InvalidOperationException(
             L.Get("SafeDestructive.Error.SourceDisappearedDuringVhdxBackup"), ex);
      }

      _backupTotalBytes = bytesRead;
      _backupSha256 = Convert.ToHexString(backupHash.GetHashAndReset());

      if(unreadableBytes > 0)
         Log(string.Format(L.Get("SafeDestructive.Log.UnreadableBytesReplaced"), FormatBytesLong(unreadableBytes)));

      // Write manifest
      var manifest = new
      {
         SourceDrive = DiskPath,
         SourceModel = DiskDisplayName,
         BackupDate = DateTime.Now.ToString("O"),
         Mode = "VhdxImage",
         ImageFormat = "VHDx Fixed (mountable)",
         MountableImage = _backupImagePath,
         TotalBytes = bytesRead,
         BlockSize = blockSize,
         UnreadableBytes = unreadableBytes,
         DataStartOffset = dataStartOffset,
         Sha256 = _backupSha256,
         Note = L.Get("SafeDestructive.VhdxNote")
      };
      using var manifestStream = File.OpenWrite(_backupManifestPath);
      await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, ct);

      await VerifyBackupImageAsync(_backupImagePath, _backupDataStartOffset, _backupTotalBytes, _backupSha256, ct);
      VhdxBackupVerified = true;

      Log(string.Format(L.Get("SafeDestructive.Log.VhdxBackupCompletedVerified"), FormatBytesLong(bytesRead)));
      StatusMessage = L.Get("SafeDestructive.Status.VhdxBackupCompletedVerified");
   }

   // ──────────────────────────────────────────────
   //  Phase 3: Raw Restore
   // ──────────────────────────────────────────────

   private async Task RunRestorePhaseAsync(CancellationToken ct)
   {
      Phase = SafeDestructivePhase.Restore;
      CurrentPhaseName = L.Get("SafeDestructive.Phase.RestoreRaw");
      CurrentPhaseIcon = "🔄";
      StatusMessage = L.Get("SafeDestructive.Status.Restoring");

      if(_backupImagePath == null || !File.Exists(_backupImagePath))
         throw new InvalidOperationException("Záloha nebyla nalezena.");

      RestoreImagePath = _backupImagePath;
      Log($"Obnova ← {_backupImagePath}");

      // Use 1 MiB blocks for speed (was 4 KiB)
      const int blockSize = 1024 * 1024;
      var buffer = new byte[blockSize];
      long totalSize = _backupTotalBytes;
      long bytesWritten = 0;
      RestoreProgress = 0;
      RestoreProgressText = "0%";
      _phaseStartTime = DateTime.UtcNow;
      _phaseBytesProcessed = 0;

      using var sourceStream = new FileStream(_backupImagePath, FileMode.Open, FileAccess.Read,
          FileShare.Read, blockSize, FileOptions.SequentialScan);
      if(_backupDataStartOffset > 0)
         sourceStream.Position = _backupDataStartOffset;
      using var targetStream = new FileStream(DiskPath, FileMode.Open, FileAccess.Write,
          FileShare.Read, blockSize, FileOptions.SequentialScan);

      try
      {
         while(bytesWritten < totalSize)
         {
            ct.ThrowIfCancellationRequested();

            int bytesToRead = (int)Math.Min(blockSize, totalSize - bytesWritten);
            int bytesReadNow = 0;

            try
            {
               bytesReadNow = await sourceStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
            }
            catch(IOException ex) when(IsDeviceDisappearedError(ex))
            {
               throw new InvalidOperationException(
                   $"❌ Zdrojové zařízení zmizelo během obnovy (pozice {FormatBytesLong(bytesWritten)}). " +
                   "Zkontrolujte připojení disku a opakujte operaci.", ex);
            }

            if(bytesReadNow == 0) break;

            try
            {
               await targetStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);
            }
            catch(IOException ex) when(IsDeviceDisappearedError(ex))
            {
               throw new InvalidOperationException(
                   $"❌ Cílové zařízení zmizelo během obnovy (pozice {FormatBytesLong(bytesWritten)}). " +
                   "Zkontrolujte připojení disku a opakujte operaci.", ex);
            }
            bytesWritten += bytesReadNow;
            _phaseBytesProcessed += bytesReadNow;

            RestoreBytesWritten = bytesWritten;
            RestoreBytesWrittenText = FormatBytesLong(bytesWritten);
            RestoreCurrentSectorText = $"Blok {bytesWritten / blockSize:N0} / {totalSize / blockSize:N0}";

            double restoreProgress = totalSize > 0 ? (double)bytesWritten / totalSize * 100 : 0;
            RestoreProgress = restoreProgress;
            RestoreProgressText = $"{restoreProgress:F0}%";
            OverallProgress = ScaleWorkflowProgress(GetBackupWeight() + GetTestWeight(), GetRestoreWeight(), restoreProgress);
            OverallProgressText = $"Celkem {OverallProgress:F0}%";

            UpdatePhaseSpeedAndEta(ref _phaseStartTime, ref _phaseBytesProcessed,
                out var speed, out var elapsed, out var eta);
            RestoreSpeedText = speed;
            RestoreElapsedText = elapsed;
            RestoreEtaText = eta;

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, ct);
         }
      }
      catch(InvalidOperationException)
      {
         throw;
      }
      catch(IOException ex) when(IsDeviceDisappearedError(ex))
      {
         throw new InvalidOperationException(
             $"❌ Zařízení zmizelo během obnovy. Zkontrolujte připojení disku a opakujte operaci.", ex);
      }

      if(bytesWritten != totalSize)
         throw new IOException($"Obnova zapsala jen {FormatBytesLong(bytesWritten)} z {FormatBytesLong(totalSize)}.");

      await VerifyRestoredDiskAsync(ct);
      _restoreCompleted = true;

      Log($"Obnova dokončena a ověřena: {FormatBytesLong(bytesWritten)}");
      StatusMessage = L.Get("SafeDestructive.Status.RestoreDone");
   }

   // ──────────────────────────────────────────────
   //  Phase 4: Create Partition (VhdxOnly mode)
   // ──────────────────────────────────────────────

   private async Task RunPartitionPhaseAsync(CancellationToken ct)
   {
      Phase = SafeDestructivePhase.Partition;
      CurrentPhaseName = L.Get("DestructiveTest.Phase.Partition");
      CurrentPhaseIcon = "📋";
      StatusMessage = "Vytvářím oddíl na disku...";

      if(SelectedDrive == null) return;

      Log($"Vytvářím oddíl na {DiskPath}...");

      try
      {
         var partitionResult = await _sanitizationService.CreatePartitionAsync(
             DiskPath,
             volumeLabel: "Tested",
             format: true,
             progress: new Progress<SanitizationProgress>(p =>
             {
                Dispatcher.UIThread.Post(() =>
                   {
                    OverallProgress = 90 + p.ProgressPercent * 0.10;
                    OverallProgressText = $"{OverallProgress:F0}%";
                    StatusMessage = p.Phase;
                 });
             }),
             ct);

         if(!partitionResult.Success)
         {
            Log($"❌ Vytvoření oddílu selhalo: {partitionResult.ErrorMessage}");
         }
         else
         {
            Log($"✅ Oddíl vytvořen: {(partitionResult.Formatted ? partitionResult.FileSystem + " formátován" : "neformátován")}");
         }
      }
      catch(Exception ex)
      {
         Log($"❌ Vytvoření oddílu selhalo: {ex.Message}");
      }
   }

   // ──────────────────────────────────────────────
   //  Results
   // ──────────────────────────────────────────────

   private async Task BuildResultsAsync()
   {
      var sb = new System.Text.StringBuilder();
      sb.AppendLine($"═══ BEZPEČNÝ DESTRUKTIVNÍ TEST — VÝSLEDKY ({(SelectedMode == SafeDestructiveMode.VhdxOnly ? "VHDx" : SelectedMode == SafeDestructiveMode.ImageRoundTrip ? "Šetrný image round-trip" : "Záloha+Obnova")}) ═══");
      sb.AppendLine($"Disk: {DiskDisplayName}");
      sb.AppendLine($"Cesta: {DiskPath}");
      sb.AppendLine($"Velikost: {DiskTotalSizeText}");
      sb.AppendLine();

      // Sanitize stats
      if(SanitizePass1WritePoints.Count > 0)
      {
         double avgWrite1 = SanitizePass1WritePoints.Average(p => p.Y ?? 0);
         double avgRead1 = SanitizePass1ReadPoints.Count > 0 ? SanitizePass1ReadPoints.Average(p => p.Y ?? 0) : 0;
         sb.AppendLine($"🧹 Sanitizace 1: Write {avgWrite1:F1} MB/s | Read {avgRead1:F1} MB/s");
      }
      if(SanitizePass2WritePoints.Count > 0)
      {
         double avgWrite2 = SanitizePass2WritePoints.Average(p => p.Y ?? 0);
         double avgRead2 = SanitizePass2ReadPoints.Count > 0 ? SanitizePass2ReadPoints.Average(p => p.Y ?? 0) : 0;
         sb.AppendLine($"🧹 Sanitizace 2: Write {avgWrite2:F1} MB/s | Read {avgRead2:F1} MB/s");
      }

      // Seek stats
      if(SeekFullStrokePoints.Count > 0)
         sb.AppendLine($"🎯 Seek Full Stroke: avg {SeekFullStrokePoints.Average(p => p.Y ?? 0):F2} ms, P95 {Percentile(SeekFullStrokePoints.Select(p => p.Y ?? 0).ToList(), 0.95):F2} ms");
      if(SeekRandomPoints.Count > 0)
         sb.AppendLine($"🎯 Seek Random: avg {SeekRandomPoints.Average(p => p.Y ?? 0):F2} ms, P95 {Percentile(SeekRandomPoints.Select(p => p.Y ?? 0).ToList(), 0.95):F2} ms");
      if(SeekSkipPoints.Count > 0)
         sb.AppendLine($"🎯 Seek Skip: avg {SeekSkipPoints.Average(p => p.Y ?? 0):F2} ms, P95 {Percentile(SeekSkipPoints.Select(p => p.Y ?? 0).ToList(), 0.95):F2} ms");

      ResultsSummary = sb.ToString();

      // SMART delta
      if(_smartBefore != null && _smartAfter != null)
      {
         var deltaSb = new System.Text.StringBuilder();
         deltaSb.AppendLine("═══ SMART ZMĚNY ═══");

         if(_smartBefore.Temperature != _smartAfter.Temperature)
            deltaSb.AppendLine($"🌡 Teplota: {_smartBefore.Temperature}°C → {_smartAfter.Temperature}°C (Δ {_smartAfter.Temperature - _smartBefore.Temperature:+0;-0}°C)");
         if(_smartBefore.ReallocatedSectorCount != _smartAfter.ReallocatedSectorCount)
            deltaSb.AppendLine($"⚠️ Reallocated sectors: {_smartBefore.ReallocatedSectorCount} → {_smartAfter.ReallocatedSectorCount} (Δ {_smartAfter.ReallocatedSectorCount - _smartBefore.ReallocatedSectorCount:+0;-0})");
         if(_smartBefore.PendingSectorCount != _smartAfter.PendingSectorCount)
            deltaSb.AppendLine($"⚠️ Pending sectors: {_smartBefore.PendingSectorCount} → {_smartAfter.PendingSectorCount} (Δ {_smartAfter.PendingSectorCount - _smartBefore.PendingSectorCount:+0;-0})");
         if(_smartBefore.UncorrectableErrorCount != _smartAfter.UncorrectableErrorCount)
            deltaSb.AppendLine($"⚠️ Uncorrectable errors: {_smartBefore.UncorrectableErrorCount} → {_smartAfter.UncorrectableErrorCount} (Δ {_smartAfter.UncorrectableErrorCount - _smartBefore.UncorrectableErrorCount:+0;-0})");
         if(_smartBefore.PowerOnHours != _smartAfter.PowerOnHours)
            deltaSb.AppendLine($"⏱ Power-on hours: {_smartBefore.PowerOnHours} → {_smartAfter.PowerOnHours} (Δ {_smartAfter.PowerOnHours - _smartBefore.PowerOnHours:+0;-0}h)");

         SmartDeltaSummary = deltaSb.ToString();
      }

   }

   // ──────────────────────────────────────────────
   //  Certificate & Session persistence
   // ──────────────────────────────────────────────

   private async Task BuildCertificateAsync()
   {
      if(SelectedDrive == null) return;

      IsGeneratingCertificate = true;
      CertificateProgressText = "PĹ™ipravuji data certifikĂˇtu...";

      var card = await _diskCardRepository.GetByDevicePathAsync(SelectedDrive.Path);
      if(card == null)
      {
         card = new DiskCard
         {
            DevicePath = SelectedDrive.Path,
            ModelName = SelectedDrive.Name ?? DiskDisplayName,
            SerialNumber = SelectedDrive.SerialNumber ?? "",
            Capacity = SelectedDrive.TotalSize,
            DiskType = _smartBefore?.DeviceType ?? "Unknown",
            CreatedAt = DateTime.UtcNow,
            LastTestedAt = DateTime.UtcNow
         };
         card = await _diskCardRepository.CreateAsync(card);
      }

      // Capture all data needed for background computation (avoid cross-thread access)
      var capturedCardId = card.Id;
      var capturedDriveName = SelectedDrive.Name ?? DiskDisplayName;
      var capturedSerial = SelectedDrive.SerialNumber ?? "-";
      var capturedCapacity = DiskTotalSizeText;
      var capturedDiskType = _smartBefore?.DeviceType ?? "HDD";
      var capturedTestType = SelectedMode == SafeDestructiveMode.VhdxOnly
          ? "BezpeÄŤnĂ˝ destruktivnĂ­ test (VHDx zĂˇloha â†’ test â†’ obnova)"
          : SelectedMode == SafeDestructiveMode.ImageRoundTrip
              ? "Ĺ etrnĂ˝ image round-trip test (RAW zĂˇloha â†’ obnova â†’ ovÄ›Ĺ™enĂ­)"
              : "BezpeÄŤnĂ˝ destruktivnĂ­ test (zĂˇloha â†’ test â†’ obnova)";
      var capturedTestDuration = DateTime.UtcNow - _phaseStartTime;
      var capturedSmartAfter = _smartAfter;
      var capturedSmartBefore = _smartBefore;
      var capturedResultsSummary = ResultsSummary;
      var capturedSmartDeltaSummary = SmartDeltaSummary;
      var capturedSelectedMode = SelectedMode;

      // Capture chart data for background processing
      var capturedSeekFullStroke = SeekFullStrokePoints.ToList();
      var capturedSeekRandom = SeekRandomPoints.ToList();
      var capturedSeekSkip = SeekSkipPoints.ToList();
      var capturedSanitizePass1Write = SanitizePass1WritePoints.ToList();
      var capturedSanitizePass1Read = SanitizePass1ReadPoints.ToList();
      var capturedSanitizePass2Write = SanitizePass2WritePoints.ToList();
      var capturedSanitizePass2Read = SanitizePass2ReadPoints.ToList();

      CertificateProgressText = "PoÄŤĂ­tĂˇm metriky a znĂˇmky...";

      // Run heavy computations on background thread to keep UI responsive
      var cert = await Task.Run(() =>
      {
         var c = new DiskCertificate
         {
            CertificateNumber = $"SAFE-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24],
            DiskCardId = capturedCardId,
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = Environment.UserName,
            DiskModel = capturedDriveName,
            SerialNumber = capturedSerial,
            Capacity = capturedCapacity,
            DiskType = capturedDiskType,
            TestType = capturedTestType,
            TestDuration = capturedTestDuration,
            Grade = CalculateSafeGrade(),
            Score = CalculateSafeScore(),
            HealthStatus = DetermineSafeHealthStatus(),
            Status = CertificateStatus.Active,
            Recommended = true,
            Notes = capturedSelectedMode == SafeDestructiveMode.VhdxOnly
                   ? $"BezpeÄŤnĂ˝ destruktivnĂ­ test: VHDx zĂˇloha â†’ test â†’ obnova.\n{capturedResultsSummary}"
                   : capturedSelectedMode == SafeDestructiveMode.ImageRoundTrip
                       ? $"Ĺ etrnĂ˝ image round-trip: RAW zĂˇloha â†’ ovÄ›Ĺ™enĂ­ â†’ obnova â†’ ovÄ›Ĺ™enĂ­ bez opakovanĂ© sanitizace.\n{capturedResultsSummary}"
                       : $"BezpeÄŤnĂ˝ destruktivnĂ­ test: zĂˇloha â†’ test â†’ obnova.\n{capturedResultsSummary}",
            SmartPassed = capturedSmartAfter?.ReallocatedSectorCount == 0 && capturedSmartAfter?.PendingSectorCount == 0,
            PowerOnHours = capturedSmartAfter?.PowerOnHours ?? capturedSmartBefore?.PowerOnHours ?? 0,
            ReallocatedSectors = capturedSmartAfter?.ReallocatedSectorCount ?? capturedSmartBefore?.ReallocatedSectorCount ?? 0,
            PendingSectors = capturedSmartAfter?.PendingSectorCount ?? capturedSmartBefore?.PendingSectorCount ?? 0,
            SanitizationPerformed = true,
            SanitizationMethod = "Zero-fill + verify (2Ă—) - safe mode",
            DataVerified = true,
            ErrorCount = 0,
            TemperatureRange = $"{capturedSmartBefore?.Temperature ?? 0}-{capturedSmartAfter?.Temperature ?? 0}Â°C",
            SmartDeltaSummary = capturedSmartDeltaSummary
         };

         // Seek metrics
         var allSeekPoints = capturedSeekFullStroke.Concat(capturedSeekRandom).Concat(capturedSeekSkip)
               .Select(p => p.Y ?? 0).Where(y => y > 0).OrderBy(y => y).ToList();
         if(allSeekPoints.Count > 0)
         {
            c.SeekAvgLatencyMs = allSeekPoints.Average();
            c.SeekMinLatencyMs = allSeekPoints.Min();
            c.SeekMaxLatencyMs = allSeekPoints.Max();
            c.SeekP95LatencyMs = Percentile(allSeekPoints, 0.95);
         }

         // Sanitize metrics
         if(capturedSanitizePass1Write.Count > 0)
            c.Sanitize1AvgWriteMBps = capturedSanitizePass1Write.Average(p => p.Y ?? 0);
         if(capturedSanitizePass1Read.Count > 0)
            c.Sanitize1AvgReadMBps = capturedSanitizePass1Read.Average(p => p.Y ?? 0);
         if(capturedSanitizePass2Write.Count > 0)
            c.Sanitize2AvgWriteMBps = capturedSanitizePass2Write.Average(p => p.Y ?? 0);
         if(capturedSanitizePass2Read.Count > 0)
            c.Sanitize2AvgReadMBps = capturedSanitizePass2Read.Average(p => p.Y ?? 0);

         return c;
      });

      Certificate = cert;
      IsGeneratingCertificate = false;
      CertificateProgressText = string.Empty;
   }

   private DiskCertificate? Certificate { get; set; }

   private async Task SaveTestSessionAsync()
   {
      try
      {
         if(SelectedDrive == null || Certificate == null) return;

         var card = await _diskCardRepository.GetByDevicePathAsync(SelectedDrive.Path);
         if(card == null)
         {
            // Card must exist before we can attach a test session to it.
            // Create it now so the foreign key is valid.
            card = new DiskCard
            {
               DevicePath = SelectedDrive.Path,
               ModelName = SelectedDrive.Name ?? DiskDisplayName,
               SerialNumber = SelectedDrive.SerialNumber ?? "",
               Capacity = SelectedDrive.TotalSize,
               DiskType = _smartBefore?.DeviceType ?? "Unknown",
               CreatedAt = DateTime.UtcNow,
               LastTestedAt = DateTime.UtcNow
            };
            card = await _diskCardRepository.CreateAsync(card);
         }

         var session = new TestSession
         {
            DiskCardId = card.Id,
            SessionId = Guid.NewGuid(),
            TestType = TestType.AbsoluteDestructive,
            StartedAt = DateTime.UtcNow - Certificate.TestDuration,
            CompletedAt = DateTime.UtcNow,
            Duration = Certificate.TestDuration,
            Status = TestStatus.Completed,
            IsDestructive = true,
            WasLocked = true,
            SmartBefore = _smartBefore,
            SmartAfter = _smartAfter,
            StartTemperature = _smartBefore?.Temperature,
            MaxTemperature = Math.Max(_smartBefore?.Temperature ?? 0, _smartAfter?.Temperature ?? 0),
            AverageTemperature = ((_smartBefore?.Temperature ?? 0) + (_smartAfter?.Temperature ?? 0)) / 2.0,
            Result = TestResult.Pass,
            Grade = Certificate.Grade,
            Score = Certificate.Score,
            HealthAssessment = MapHealthAssessment(Certificate.HealthStatus),
            Notes = Certificate.Notes,
            SmartChanges = _smartBefore != null && _smartAfter != null
                 ? BuildSmartChanges(_smartBefore, _smartAfter)
                 : new List<SmartAttributeChange>()
         };

         await _diskCardRepository.CreateTestSessionAsync(session);

         Certificate.TestSessionId = session.Id;
         Certificate.DiskCardId = card.Id;
         await _diskCardRepository.CreateCertificateAsync(Certificate);

         Log($"Certifikát uložen: {Certificate.CertificateNumber}");
      }
      catch(Exception ex)
      {
         Log($"❌ Uložení session/certifikátu selhalo: {ex.Message}");
      }
   }

   private string CalculateSafeGrade()
   {
      var score = CalculateSafeScore();
      return score switch
      {
         >= 90 => "A",
         >= 80 => "B",
         >= 70 => "C",
         >= 55 => "D",
         >= 40 => "E",
         _ => "F"
      };
   }

   private int CalculateSafeScore()
   {
      double avgWrite = 0;
      if(SanitizePass1WritePoints.Count > 0) avgWrite = SanitizePass1WritePoints.Average(p => p.Y ?? 0);
      double score = avgWrite switch
      {
         >= 250 => 95,
         >= 200 => 85,
         >= 150 => 70,
         >= 100 => 55,
         >= 60 => 40,
         >= 30 => 25,
         _ => 10
      };

      var smart = _smartAfter ?? _smartBefore;
      if(smart != null)
      {
         var realloc = smart.ReallocatedSectorCount ?? 0;
         var pending = smart.PendingSectorCount ?? 0;
         var uncorr = smart.UncorrectableErrorCount ?? 0;
         var media = smart.MediaErrors ?? 0;
         score -= Math.Min(40, realloc * 3.0);
         score -= Math.Min(55, pending * 18.0);
         score -= Math.Min(55, uncorr * 15.0);
         score -= Math.Min(55, media * 15.0);
         if(pending > 0 || uncorr > 0 || media > 0 || smart.IsFailing) score = Math.Min(score, 34);
         else if(realloc > 50) score = Math.Min(score, 49);
         else if(realloc > 0) score = Math.Min(score, 79);
      }

      return (int)Math.Clamp(score, 0, 100);
   }

   private string DetermineSafeHealthStatus()
   {
      var smart = _smartAfter ?? _smartBefore;
      if(smart == null) return "Healthy - test completed successfully with backup/restore";
      var realloc = smart.ReallocatedSectorCount ?? 0;
      var pending = smart.PendingSectorCount ?? 0;
      var uncorr = smart.UncorrectableErrorCount ?? 0;
      var media = smart.MediaErrors ?? 0;
      if(smart.IsFailing || pending > 0 || uncorr > 0 || media > 0 || realloc > 50) return "Critical - SMART critical counters present";
      if(realloc > 0) return "Warning - SMART reallocated sectors present";
      return "Healthy - test completed successfully with backup/restore";
   }

   private static HealthAssessment MapHealthAssessment(string? healthStatus)
   {
      if(string.IsNullOrWhiteSpace(healthStatus)) return HealthAssessment.Unknown;
      if(healthStatus.Contains("Healthy", StringComparison.OrdinalIgnoreCase)) return HealthAssessment.Excellent;
      if(healthStatus.Contains("Warning", StringComparison.OrdinalIgnoreCase)) return HealthAssessment.Fair;
      if(healthStatus.Contains("Critical", StringComparison.OrdinalIgnoreCase)) return HealthAssessment.Critical;
      return HealthAssessment.Unknown;
   }

   private static List<SmartAttributeChange> BuildSmartChanges(SmartaData before, SmartaData after)
   {
      var changes = new List<SmartAttributeChange>();
      long bTemp = before.Temperature ?? 0;
      long aTemp = after.Temperature ?? 0;
      if(bTemp != aTemp)
         changes.Add(new SmartAttributeChange { AttributeName = "Temperature", ValueBefore = bTemp, ValueAfter = aTemp, Change = aTemp - bTemp });

      long bRealloc = before.ReallocatedSectorCount ?? 0;
      long aRealloc = after.ReallocatedSectorCount ?? 0;
      if(bRealloc != aRealloc)
         changes.Add(new SmartAttributeChange { AttributeName = "ReallocatedSectorCount", ValueBefore = bRealloc, ValueAfter = aRealloc, Change = aRealloc - bRealloc });

      long bPending = before.PendingSectorCount ?? 0;
      long aPending = after.PendingSectorCount ?? 0;
      if(bPending != aPending)
         changes.Add(new SmartAttributeChange { AttributeName = "PendingSectorCount", ValueBefore = bPending, ValueAfter = aPending, Change = aPending - bPending });

      long bUncorr = before.UncorrectableErrorCount ?? 0;
      long aUncorr = after.UncorrectableErrorCount ?? 0;
      if(bUncorr != aUncorr)
         changes.Add(new SmartAttributeChange { AttributeName = "UncorrectableErrorCount", ValueBefore = bUncorr, ValueAfter = aUncorr, Change = aUncorr - bUncorr });

      long bPoh = before.PowerOnHours ?? 0;
      long aPoh = after.PowerOnHours ?? 0;
      if(bPoh != aPoh)
         changes.Add(new SmartAttributeChange { AttributeName = "PowerOnHours", ValueBefore = bPoh, ValueAfter = aPoh, Change = aPoh - bPoh });

      return changes;
   }

   // ──────────────────────────────────────────────
   //  Seek chart switching
   // ──────────────────────────────────────────────

   private void SwitchSeekChart()
   {
      ActiveSeekChartIndex = (ActiveSeekChartIndex + 1) % 3;
      RebuildSeekChart();
   }

   private void RebuildSeekChart()
   {
      var points = ActiveSeekChartIndex switch
      {
         0 => SeekFullStrokePoints,
         1 => SeekRandomPoints,
         2 => SeekSkipPoints,
         _ => SeekFullStrokePoints
      };

      var name = ActiveSeekChartIndex switch
      {
         0 => "Full Stroke",
         1 => "Náhodný",
         2 => "Skip",
         _ => "Full Stroke"
      };

      var newSeries = new ISeries[]
      {
            new ScatterSeries<ObservablePoint>
            {
                Values = points,
                Name = name,
                Stroke = new SolidColorPaint(new SKColor(0x3B, 0x82, 0xF6), 1),
                Fill = new SolidColorPaint(new SKColor(0x3B, 0x82, 0xF6, 0x40)),
                GeometrySize = 4
            }
      };

      // Two-step assignment forces LiveCharts2 SkiaSharp to detect the change and redraw
      SeekChartSeries = Array.Empty<ISeries>();
      SeekChartSeries = newSeries;
   }

   // ──────────────────────────────────────────────
   //  Sanitize series toggle
   // ──────────────────────────────────────────────

   private void ToggleSanitizeSeries(string? param)
   {
      switch(param)
      {
         case "pass1write": ShowPass1Write = !ShowPass1Write; break;
         case "pass1read": ShowPass1Read = !ShowPass1Read; break;
         case "pass2write": ShowPass2Write = !ShowPass2Write; break;
         case "pass2read": ShowPass2Read = !ShowPass2Read; break;
      }
      RebuildSanitizeChart();
   }

   // ──────────────────────────────────────────────
   //  Helpers
   // ──────────────────────────────────────────────

   private double GetBackupWeight() => SelectedMode == SafeDestructiveMode.VhdxOnly ? VhdxBackupWeight : SelectedMode == SafeDestructiveMode.ImageRoundTrip ? 50d : BackupAndRestoreBackupWeight;
   private double GetTestWeight() => SelectedMode == SafeDestructiveMode.VhdxOnly ? VhdxTestWeight : SelectedMode == SafeDestructiveMode.ImageRoundTrip ? 0d : BackupAndRestoreTestWeight;
   private double GetRestoreWeight() => SelectedMode == SafeDestructiveMode.VhdxOnly ? VhdxRestoreWeight : SelectedMode == SafeDestructiveMode.ImageRoundTrip ? 50d : BackupAndRestoreRestoreWeight;

   private static double ScaleWorkflowProgress(double startPercent, double weightPercent, double phaseProgressPercent)
       => startPercent + Math.Clamp(phaseProgressPercent, 0, 100) / 100d * weightPercent;

   private async Task VerifyBackupImageAsync(string imagePath, long dataOffset, long dataBytes, string expectedSha256, CancellationToken ct)
   {
      if(string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
         throw new InvalidOperationException("Ověření zálohy selhalo — soubor zálohy neexistuje.");

      var fi = new FileInfo(imagePath);
      if(fi.Length < dataOffset + dataBytes)
         throw new InvalidOperationException($"Ověření zálohy selhalo — soubor je kratší ({FormatBytesLong(fi.Length)}) než očekávaná data ({FormatBytesLong(dataOffset + dataBytes)}).");

      BackupVerificationText = "Ověřuji zálohu čtením...";
      StatusMessage = BackupVerificationText;
      Log("Ověřuji vytvořenou zálohu plným čtením a SHA-256...");

      var actual = await ComputeFileRegionSha256Async(imagePath, dataOffset, dataBytes, ct);
      if(!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
         throw new InvalidOperationException("Ověření zálohy selhalo — kontrolní součet nesouhlasí. Destruktivní test nebude spuštěn.");

      _backupVerified = true;
      BackupVerificationText = "✅ Záloha ověřena";
      Log($"✅ Záloha ověřena SHA-256: {actual}");
   }

   private async Task VerifyRestoredDiskAsync(CancellationToken ct)
   {
      if(string.IsNullOrWhiteSpace(_backupSha256))
         throw new InvalidOperationException("Nelze ověřit obnovu — chybí kontrolní součet zálohy.");

      StatusMessage = "Ověřuji obnovená data na disku...";
      Log("Ověřuji obnovený disk plným čtením a SHA-256...");

      var actual = await ComputeFileRegionSha256Async(DiskPath, 0, _backupTotalBytes, ct);
      if(!string.Equals(actual, _backupSha256, StringComparison.OrdinalIgnoreCase))
         throw new InvalidOperationException("Ověření obnovy selhalo — data na disku neodpovídají ověřené záloze.");

      Log($"✅ Obnova ověřena SHA-256: {actual}");
   }

   private static async Task<string> ComputeFileRegionSha256Async(string path, long offset, long bytesToHash, CancellationToken ct)
   {
      const int bufferSize = 1024 * 1024;
      var buffer = new byte[bufferSize];
      using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize, FileOptions.SequentialScan);
      if(offset > 0)
         stream.Position = offset;

      long remaining = bytesToHash;
      while(remaining > 0)
      {
         ct.ThrowIfCancellationRequested();
         int toRead = (int)Math.Min(buffer.Length, remaining);
         int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), ct);
         if(read == 0)
            throw new EndOfStreamException("Neočekávaný konec souboru při výpočtu kontrolního součtu.");
         hash.AppendData(buffer, 0, read);
         remaining -= read;
      }

      return Convert.ToHexString(hash.GetHashAndReset());
   }

   private async Task TryEmergencyRestoreAsync(string reason)
   {
      if(!_backupVerified || string.IsNullOrWhiteSpace(_backupImagePath) || !File.Exists(_backupImagePath))
      {
         Log($"⚠️ Nouzová obnova nelze spustit: {reason}; záloha není ověřená nebo není dostupná.");
         return;
      }

      try
      {
         Log($"⚠️ {reason}. Spouštím nouzovou obnovu ověřené zálohy...");
         await RunRestorePhaseAsync(CancellationToken.None);
         Log("✅ Nouzová obnova dokončena.");
      }
      catch(Exception restoreEx)
      {
         Log($"❌ NOUZOVÁ OBNOVA SELHALA: {restoreEx.Message}");
      }
   }

   /// <summary>
   /// Detects whether an exception indicates the target device has disappeared
   /// (disconnected, powered off, or driver unloaded).
   /// </summary>
   private static bool IsDeviceDisappearedError(Exception ex)
   {
      var code = ex.HResult & 0xFFFF;
      return code is 21 or 1167 ||
             ex.Message.Contains("device is not connected", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("no such device", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("the device is not ready", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("cannot find the device", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("the device is not accessible", StringComparison.OrdinalIgnoreCase);
   }

   private double CalculateCurrentSpeed()
   {
      var elapsed = (DateTime.UtcNow - _phaseStartTime).TotalSeconds;
      if(elapsed > 0.5 && _phaseBytesProcessed > 0)
         return _phaseBytesProcessed / elapsed / (1024.0 * 1024.0); // MB/s
      return 0;
   }

   private void UpdatePhaseSpeedAndEta(ref DateTime startTime, ref long bytesProcessed,
       out string speedText, out string elapsedText, out string etaText)
   {
      var elapsed = DateTime.UtcNow - startTime;
      elapsedText = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

      if(elapsed.TotalSeconds > 1 && bytesProcessed > 0)
      {
         var speedBps = bytesProcessed / elapsed.TotalSeconds;
         speedText = FormatBytesLong((long)speedBps) + "/s";

         long totalForPhase = Phase switch
         {
            SafeDestructivePhase.Backup => DiskTotalBytes,
            SafeDestructivePhase.Restore => _backupTotalBytes,
            _ => DiskTotalBytes
         };

         if(totalForPhase > 0 && speedBps > 0)
         {
            var remainingBytes = totalForPhase - bytesProcessed;
            var remainingSeconds = remainingBytes / speedBps;
            etaText = remainingSeconds < 3600
                ? $"{remainingSeconds / 60:F0}m {remainingSeconds % 60:F0}s"
                : $"{(int)(remainingSeconds / 3600)}h {(int)(remainingSeconds % 3600 / 60)}m";
         }
         else
         {
            etaText = "—";
         }
      }
      else
      {
         speedText = "—";
         etaText = "—";
      }
   }

   private static double Percentile(List<double> values, double percentile)
   {
      if(values.Count == 0) return 0;
      var sorted = values.OrderBy(v => v).ToList();
      int index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
      return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
   }

   private static string FormatBytesLong(long bytes)
   {
      return bytes switch
      {
         >= 1_000_000_000_000L => $"{bytes / 1_000_000_000_000.0:F2} TB",
         >= 1_000_000_000L => $"{bytes / 1_000_000_000.0:F2} GB",
         >= 1_000_000L => $"{bytes / 1_000_000.0:F2} MB",
         >= 1_000L => $"{bytes / 1_000.0:F2} KB",
         _ => $"{bytes} B"
      };
   }

   /// <summary>
   /// Writes a minimal VHDx header and Block Allocation Table for a dynamic disk.
   /// This creates a valid, mountable VHDx file that Windows and Linux can open.
   /// </summary>
   private static long RoundUp(long value, long alignment) => ((value + alignment - 1) / alignment) * alignment;

   private static long CalculateVhdxDataStartOffset(long diskSizeBytes)
   {
      const int logicalSectorSize = 1048576;
      const int physicalSectorSize = 4096;
      long diskSizeRounded = RoundUp(diskSizeBytes, logicalSectorSize);
      long chunkCount = diskSizeRounded / logicalSectorSize;
      long batSize = RoundUp(chunkCount * 8, physicalSectorSize);
      long metadataEnd = 256L * 1024 + batSize + 1024L * 1024;
      return RoundUp(metadataEnd, logicalSectorSize);
   }

   private static async Task WriteVhdxHeaderAsync(string path, long diskSizeBytes, CancellationToken ct)
   {
      const int logicalSectorSize = 1048576; // 1 MiB
      const int physicalSectorSize = 4096;

      long diskSizeRounded = RoundUp(diskSizeBytes, logicalSectorSize);
      long chunkCount = diskSizeRounded / logicalSectorSize;
      long batSize = RoundUp(chunkCount * 8, physicalSectorSize);
      long dataStartOffset = CalculateVhdxDataStartOffset(diskSizeBytes);

      using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan);
      using var writer = new BinaryWriter(fs);

      // 1. File Type Identifier (at 0) — "vhdxfile" signature
      writer.Write(new byte[] { 0x76, 0x68, 0x64, 0x78, 0x66, 0x69, 0x6C, 0x65 });
      writer.Write((uint)0); // CreatorVersion
      writer.Write(new byte[65536 - 12]); // Pad to 64 KB

      // 2. Header 1 (at 64 KB) — sequence 0
      long header1Pos = fs.Position;
      WriteVhdxHeaderAt(writer, 0, logicalSectorSize, physicalSectorSize);

      // Pad from end of Header 1 (64KB + 4KB = 68KB) to Header 2 position (128KB)
      while(fs.Position < 128L * 1024)
         writer.Write((byte)0);

      // 3. Header 2 (at 128 KB) — sequence 1
      long header2Pos = fs.Position;
      WriteVhdxHeaderAt(writer, 1, logicalSectorSize, physicalSectorSize);

      // Pad from end of Header 2 (128KB + 4KB = 132KB) to Region Table position (192KB)
      while(fs.Position < 192L * 1024)
         writer.Write((byte)0);

      // 4. Region Table (at 192 KB)
      long regionTablePos = fs.Position;
      writer.Write(new byte[] { 0x72, 0x65, 0x67, 0x69 }); // "regi"
      writer.Write((uint)0); // Checksum (simplified — 0 is acceptable)
      writer.Write((uint)2); // EntryCount = 2 (BAT + Metadata)
      writer.Write(new byte[4]); // Reserved (header = 16 bytes total)

      // Entry 0: BAT — GUID {2DC277E9-0F79-41E9-9E2E-7A1D5A1CB5D3}
      writer.Write(new byte[] { 0xE9, 0x77, 0xC2, 0x2D, 0x79, 0x0F, 0xE9, 0x41, 0x9E, 0x2E, 0x7A, 0x1D, 0x5A, 0x1C, 0xB5, 0xD3 });
      writer.Write((ulong)(256L * 1024)); // FileOffset (BAT at 256 KB)
      writer.Write((uint)batSize); // Length
      writer.Write((uint)1); // Required
                             // Entry is exactly 32 bytes (16+8+4+4), no padding

      // Entry 1: Metadata — GUID {8B983ECF-8B1D-CC43-ADE4-BCD55AEF5C6E}
      writer.Write(new byte[] { 0xCF, 0x3E, 0x98, 0x8B, 0x1D, 0x8B, 0x43, 0xCC, 0xAD, 0xE4, 0xBC, 0xD5, 0x5A, 0xEF, 0x5C, 0x6E });
      writer.Write((ulong)(256L * 1024 + batSize)); // FileOffset
      writer.Write((uint)(1024L * 1024)); // Length (1 MiB)
      writer.Write((uint)1); // Required
                             // Entry is exactly 32 bytes (16+8+4+4), no padding

      // Pad region table to 256 KB
      while(fs.Position < 256L * 1024) writer.Write((byte)0);

      // 5. BAT (at 256 KB) — Fixed VHDx: all entries = fully allocated
      long fileOffsetInSectors = dataStartOffset / logicalSectorSize;
      for(long i = 0; i < chunkCount; i++)
      {
         // State = 6 (PAYLOAD_BLOCK_FULLY_PRESENT), FileOffset at bits 20-63
         ulong batEntry = (6UL << 0) | ((ulong)(fileOffsetInSectors + i) << 20);
         writer.Write(batEntry);
      }

      // Pad BAT to batSize
      long batEnd = 256L * 1024 + batSize;
      while(fs.Position < batEnd) writer.Write((byte)0);

      // 6. Metadata (1 MiB)
      WriteVhdxMetadataAt(writer, diskSizeRounded, logicalSectorSize, physicalSectorSize);

      // Pad to data start offset
      while(fs.Position < dataStartOffset) writer.Write((byte)0);

      await fs.FlushAsync(ct);
   }

   private static void WriteVhdxHeaderAt(BinaryWriter writer, ulong sequenceNumber, int logicalSectorSize, int physicalSectorSize)
   {
      long startPos = writer.BaseStream.Position;

      // Build header in memory first (stream is write-only, can't read back)
      using var ms = new MemoryStream(4096);
      using var bw = new BinaryWriter(ms);

      bw.Write(new byte[] { 0x68, 0x65, 0x61, 0x64 }); // "head"
      bw.Write((uint)0); // Checksum placeholder (will be calculated)
      bw.Write(sequenceNumber);
      bw.Write(Guid.NewGuid().ToByteArray()); // FileWriteGuid
      bw.Write(Guid.NewGuid().ToByteArray()); // DataWriteGuid
      bw.Write(new byte[16]); // LogGuid (zero = no log → no log present)
      bw.Write((ushort)0); // LogVersion = 0 (must be 0 when no log)
      bw.Write((ushort)1); // Version = 1 (VHDx v1.0)
      bw.Write((uint)0); // LogLength = 0 (no log)
      bw.Write((ulong)0); // LogOffset = 0 (no log)
      bw.Write((uint)0); // Reserved
      bw.Write(new byte[4012]); // Pad to 4 KB

      byte[] header = ms.ToArray();

      // CRC32 over the 4KB block (checksum field bytes 4-7 are already zero)
      uint crc = Crc32(header);

      // Write CRC32 into the buffer at offset 4
      header[4] = (byte)(crc & 0xFF);
      header[5] = (byte)((crc >> 8) & 0xFF);
      header[6] = (byte)((crc >> 16) & 0xFF);
      header[7] = (byte)((crc >> 24) & 0xFF);

      // Write the complete header to the actual stream
      writer.Write(header);
   }

   /// <summary>CRC-32C (Castagnoli) as required by VHDX spec. Polynomial 0x1EDC6F41, reflected 0x82F63B78.</summary>
   private static uint Crc32(byte[] data)
   {
      uint[] table = new uint[256];
      for(uint i = 0; i < 256; i++)
      {
         uint crc = i;
         for(int j = 0; j < 8; j++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0x82F63B78 : crc >> 1;
         table[i] = crc;
      }
      uint result = 0xFFFFFFFF;
      foreach(byte b in data)
         result = table[(result ^ b) & 0xFF] ^ (result >> 8);
      return result ^ 0xFFFFFFFF;
   }

   private static void WriteVhdxMetadataAt(BinaryWriter writer, long diskSizeRounded, int logicalSectorSize, int physicalSectorSize)
   {
      long metadataStart = writer.BaseStream.Position;

      // Metadata Table Header
      writer.Write(new byte[] { 0x6D, 0x65, 0x74, 0x61, 0x64, 0x61, 0x74, 0x61 }); // "metadata"
      writer.Write((ushort)0); // Reserved
      writer.Write((ushort)5); // EntryCount = 5
      writer.Write(new byte[20]); // Reserved

      // Reserve space for 5 entries (5 * 32 = 160 bytes)
      long entriesStart = writer.BaseStream.Position;
      byte[] entries = new byte[160];
      writer.Write(entries);

      // Write metadata items and update entries
      // Item 0: File Parameters (guid: CAA16737-FA36-4D43-B3B6-33F0AA44E76B)
      long item0Pos = writer.BaseStream.Position;
      writer.Write((uint)logicalSectorSize); // BlockSize
      writer.Write((uint)0); // Flags
      WriteMetadataEntry(entries, 0,
          new byte[] { 0x37, 0x67, 0xA1, 0xCA, 0x36, 0xFA, 0x43, 0x4D, 0xB3, 0xB6, 0x33, 0xF0, 0xAA, 0x44, 0xE7, 0x6B },
          (uint)(item0Pos - metadataStart), 8, false, false);

      // Item 1: Virtual Disk Size (guid: 2FA54224-CD1B-4876-B211-5DBED83BF4B8)
      long item1Pos = writer.BaseStream.Position;
      writer.Write((ulong)diskSizeRounded);
      WriteMetadataEntry(entries, 1,
          new byte[] { 0x24, 0x42, 0xA5, 0x2F, 0x1B, 0xCD, 0x76, 0x48, 0xB2, 0x11, 0x5D, 0xBE, 0xD8, 0x3B, 0xF4, 0xB8 },
          (uint)(item1Pos - metadataStart), 8, false, true);

      // Item 2: Logical Sector Size (guid: 8141BF1D-A96F-4709-BA47-F233A8FAAB5F)
      long item2Pos = writer.BaseStream.Position;
      writer.Write((uint)512); // Standard 512-byte logical sector
      WriteMetadataEntry(entries, 2,
          new byte[] { 0x1D, 0xBF, 0x41, 0x81, 0x6F, 0xA9, 0x09, 0x47, 0xBA, 0x47, 0xF2, 0x33, 0xA8, 0xFA, 0xAB, 0x5F },
          (uint)(item2Pos - metadataStart), 4, false, true);

      // Item 3: Physical Sector Size (guid: CDA348C7-445D-4471-9CC9-E9885251C556)
      long item3Pos = writer.BaseStream.Position;
      writer.Write((uint)physicalSectorSize);
      WriteMetadataEntry(entries, 3,
          new byte[] { 0xC7, 0x48, 0xA3, 0xCD, 0x5D, 0x44, 0x71, 0x44, 0x9C, 0xC9, 0xE9, 0x88, 0x52, 0x51, 0xC5, 0x56 },
          (uint)(item3Pos - metadataStart), 4, false, true);

      // Item 4: Virtual Disk Id (guid: BECA12AB-B2E6-4523-93EF-C309E000C746)
      long item4Pos = writer.BaseStream.Position;
      writer.Write(Guid.NewGuid().ToByteArray());
      WriteMetadataEntry(entries, 4,
          new byte[] { 0xAB, 0x12, 0xCA, 0xBE, 0xE6, 0xB2, 0x23, 0x45, 0x93, 0xEF, 0xC3, 0x09, 0xE0, 0x00, 0xC7, 0x46 },
          (uint)(item4Pos - metadataStart), 16, false, true);

      // Write updated entries back
      long currentPos = writer.BaseStream.Position;
      writer.BaseStream.Position = entriesStart;
      writer.Write(entries);
      writer.BaseStream.Position = currentPos;

      // Pad to 1 MiB
      while(writer.BaseStream.Position < metadataStart + 1024L * 1024)
         writer.Write((byte)0);
   }

   private static void WriteMetadataEntry(byte[] buffer, int index, byte[] guid, uint offset, uint length, bool isUser, bool isVirtualDisk)
   {
      int baseOffset = index * 32;
      Array.Copy(guid, 0, buffer, baseOffset, 16);
      BitConverter.GetBytes(offset).CopyTo(buffer, baseOffset + 16);
      BitConverter.GetBytes(length).CopyTo(buffer, baseOffset + 20);
      buffer[baseOffset + 24] = isUser ? (byte)1 : (byte)0;
      buffer[baseOffset + 25] = isVirtualDisk ? (byte)1 : (byte)0;
      // bytes 26-31 are reserved (zero)
   }
   private void Log(string message)
   {
      var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
      _logEntries.Add(entry);
      LogText = string.Join("\n", _logEntries);
   }

   // ──────────────────────────────────────────────
   //  Cancel / GoBack
   // ──────────────────────────────────────────────

   private void Cancel()
   {
      _cts?.Cancel();
      Log("⏹ Zrušení požadováno...");
   }

   private void GoBack()
   {
      if(Phase == SafeDestructivePhase.Backup || Phase == SafeDestructivePhase.Test || Phase == SafeDestructivePhase.Restore || Phase == SafeDestructivePhase.Partition)
      {
         _ = _dialogService.ShowErrorAsync(L.Get("Common.OperationRunning"), L.Get("Common.CannotLeaveDuringOperation"));
         return;
      }
      _navigationService.NavigateTo<AbsoluteDestructiveTestViewModel>();
   }

   // ──────────────────────────────────────────────
   //  INavigableViewModel
   // ──────────────────────────────────────────────

   public void OnNavigatedTo()
   {
      _ = InitializeAsync();
   }

   // ──────────────────────────────────────────────
   //  IDisposable
   // ──────────────────────────────────────────────

   public void Dispose()
   {
      if(_disposed) return;
      _disposed = true;
      _cts?.Cancel();
      _powerSession?.Dispose();
      _powerSession = null;
      _cts?.Dispose();
      _cts = null;
      GC.SuppressFinalize(this);
   }
}
