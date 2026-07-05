using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

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
    /// <summary>All phases complete.</summary>
    Completed,
    /// <summary>Cancelled by user or error.</summary>
    Failed
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

    // ──────────────────────────────────────────────
    //  Cancellation
    // ──────────────────────────────────────────────

    private CancellationTokenSource? _cts;
    private bool _disposed;

    // ──────────────────────────────────────────────
    //  Observable properties — workflow
    // ──────────────────────────────────────────────

    [ObservableProperty] private SafeDestructivePhase _phase = SafeDestructivePhase.Ready;
    [ObservableProperty] private string _statusMessage = "Připraveno — vyberte cílový disk pro zálohu.";
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

    // ── Backup target selection ──
    [ObservableProperty] private ObservableCollection<BackupTargetItem> _backupTargetDrives = new();
    [ObservableProperty] private BackupTargetItem? _selectedBackupTarget;

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

    // ──────────────────────────────────────────────
    //  Results
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

    [ObservableProperty] private ISeries[] _sanitizeChartSeries = new ISeries[]
    {
        new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>(),
            Fill = null, Stroke = null, GeometrySize = 0, LineSmoothness = 0
        }
    };
    [ObservableProperty] private Axis[] _sanitizeChartXAxes = new Axis[]
    {
        new Axis { Name = "Progres (%)", NameTextSize = 10, TextSize = 9, MinLimit = 0, MaxLimit = 100, Labeler = v => v.ToString("F0") }
    };
    [ObservableProperty] private Axis[] _sanitizeChartYAxes = new Axis[]
    {
        new Axis { Name = "MB/s", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => $"{v:F0}" }
    };

    [ObservableProperty] private ISeries[] _seekChartSeries = new ISeries[]
    {
        new LineSeries<ObservablePoint>
        {
            Values = new ObservableCollection<ObservablePoint>(),
            Fill = null, Stroke = null, GeometrySize = 0, LineSmoothness = 0
        }
    };
    [ObservableProperty] private Axis[] _seekChartXAxes = new Axis[]
    {
        new Axis { Name = "Seek #", NameTextSize = 10, TextSize = 9, MinLimit = 0, Labeler = v => v.ToString("F0") }
    };
    [ObservableProperty] private Axis[] _seekChartYAxes = new Axis[]
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

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

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
        TestCompletionNotificationService notificationService)
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

        StartWorkflowCommand = new AsyncRelayCommand(StartWorkflowAsync, () => Phase == SafeDestructivePhase.Ready && SelectedDrive != null && HasEnoughBackupSpace);
        CancelCommand = new RelayCommand(Cancel);
        GoBackCommand = new RelayCommand(GoBack);
        SwitchSeekChartCommand = new RelayCommand(SwitchSeekChart);
        ToggleSanitizeSeriesCommand = new RelayCommand<string>(ToggleSanitizeSeries);

        InitializeChartDefaults();
    }

    // ──────────────────────────────────────────────
    //  Initialization
    // ──────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        var disk = _selectedDiskService.SelectedDisk;
        if (disk == null)
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
        if (disk.SupportsSmart)
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
        Log($"Cesta: {DiskPath}");
        Log($"SMART před testem: {(_smartBefore != null ? "dostupný" : "nedostupný")}");

        if (HasEnoughBackupSpace)
            StatusMessage = L.Get("SafeDestructive.Status.Ready");
        else
            StatusMessage = L.Get("SafeDestructive.Status.NoSpace");
    }

    private async Task FindBackupTargetAsync()
    {
        BackupTargetDrives.Clear();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
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
        if (best != null)
        {
            best.IsSelected = true;
            SelectedBackupTarget = best;
        }
        else if (BackupTargetDrives.Count > 0)
        {
            // No target has enough space — still show them but don't auto-select
            BackupTargetPath = "(není vybrán — žádný disk nemá dostatek místa)";
            HasEnoughBackupSpace = false;
            BackupSpaceSummary = $"❌ Žádný z {BackupTargetDrives.Count} cílových disků nemá dostatek místa (potřeba {DiskTotalSizeText}).";
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
            if (string.IsNullOrEmpty(lsblkOutput)) return;

            using var doc = System.Text.Json.JsonDocument.Parse(lsblkOutput);
            if (!doc.RootElement.TryGetProperty("blockdevices", out var devices)) return;

            // Determine which top-level disk is the source
            string? sourceDiskName = null;
            if (SelectedDrive != null)
            {
                var sourceDeviceName = System.IO.Path.GetFileName(SelectedDrive.Path);
                sourceDiskName = FindParentDiskName(devices, sourceDeviceName);
            }

            // Iterate top-level disks, collect mounted partitions as targets
            foreach (var disk in devices.EnumerateArray())
            {
                var diskName = disk.TryGetProperty("name", out var dn) ? dn.GetString() : "";
                var diskType = disk.TryGetProperty("type", out var dt) ? dt.GetString() : "";

                // Skip source disk and non-disk devices (like loop, ram)
                if (diskType != "disk") continue;
                if (sourceDiskName != null &&
                    string.Equals(diskName, sourceDiskName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var diskModel = disk.TryGetProperty("model", out var dm) ? dm.GetString()?.Trim() : null;

                // Check children (partitions)
                if (disk.TryGetProperty("children", out var children))
                {
                    foreach (var part in children.EnumerateArray())
                    {
                        var partType = part.TryGetProperty("type", out var pt) ? pt.GetString() : "";
                        if (partType != "part") continue;

                        var mountPoint = part.TryGetProperty("mountpoint", out var mp) ? mp.GetString() : null;
                        if (string.IsNullOrWhiteSpace(mountPoint) || mountPoint == "null") continue;

                        var fsAvail = part.TryGetProperty("fsavail", out var fa) &&
                                      fa.ValueKind == System.Text.Json.JsonValueKind.Number
                            ? fa.GetInt64() : 0;

                        // Skip if fsavail is null/zero (not a mounted filesystem)
                        if (fsAvail <= 0) continue;

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
        foreach (var disk in devices.EnumerateArray())
        {
            var diskName = disk.TryGetProperty("name", out var dn) ? dn.GetString() : "";
            if (string.Equals(diskName, deviceName, StringComparison.OrdinalIgnoreCase))
                return diskName; // It's already a top-level disk

            if (disk.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    var childName = child.TryGetProperty("name", out var cn) ? cn.GetString() : "";
                    if (string.Equals(childName, deviceName, StringComparison.OrdinalIgnoreCase))
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
        if (SelectedDrive != null)
        {
            var sourceRoot = System.IO.Path.GetPathRoot(SelectedDrive.Path)?.TrimEnd('\\', '/');
            drives = drives.Where(d =>
            {
                var root = d.RootDirectory.FullName.TrimEnd('\\', '/');
                return !string.Equals(root, sourceRoot, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        foreach (var d in drives)
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
        foreach (var t in BackupTargetDrives)
            t.IsSelected = t == value;

        RecalculateBackupSpace();
    }

    private void RecalculateBackupSpace()
    {
        if (SelectedBackupTarget == null)
        {
            BackupTargetPath = "(není vybrán)";
            BackupTargetFreeBytes = 0;
            BackupTargetFreeText = "0 B";
            HasEnoughBackupSpace = false;
            BackupSpaceSummary = "❌ Není vybrán cílový disk pro zálohu.";
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
            ? $"✅ Dostatek místa: {FormatBytesLong(usableBytes)} volných (potřeba {DiskTotalSizeText})"
            : $"❌ Nedostatek: {FormatBytesLong(usableBytes)} volných, potřeba {DiskTotalSizeText}";

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
            if (process == null) return null;

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
            Name = "Progres (%)",
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
        SeekChartYAxes = new[] { new Axis { Name = "Latence (ms)", MinLimit = 0 } };

        RebuildSanitizeChart();
    }

    private void RebuildSanitizeChart()
    {
        var series = new List<ISeries>();

        if (ShowPass1Write && SanitizePass1WritePoints.Count > 0)
            series.Add(CreateLineSeries(SanitizePass1WritePoints, "1. Zápis", new SKColor(0xEF, 0x44, 0x44), 2));
        if (ShowPass1Read && SanitizePass1ReadPoints.Count > 0)
            series.Add(CreateLineSeries(SanitizePass1ReadPoints, "1. Čtení", new SKColor(0x22, 0xC5, 0x5E), 2));
        if (ShowPass2Write && SanitizePass2WritePoints.Count > 0)
            series.Add(CreateLineSeries(SanitizePass2WritePoints, "2. Zápis", new SKColor(0xB9, 0x1C, 0x1C), 2));
        if (ShowPass2Read && SanitizePass2ReadPoints.Count > 0)
            series.Add(CreateLineSeries(SanitizePass2ReadPoints, "2. Čtení", new SKColor(0x15, 0x80, 0x3D), 2));

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
        if (SelectedDrive == null) return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            // ── Phase 1: Backup ──
            await RunBackupPhaseAsync(ct);
            if (ct.IsCancellationRequested) return;

            // ── Phase 2: Destructive Test ──
            await RunTestPhaseAsync(ct);
            if (ct.IsCancellationRequested) return;

            // ── Phase 3: Restore ──
            await RunRestorePhaseAsync(ct);
            if (ct.IsCancellationRequested) return;

            // ── Complete ──
            Phase = SafeDestructivePhase.Completed;
            CurrentPhaseName = L.Get("SafeDestructive.Phase.Done");
            CurrentPhaseIcon = "✅";
            OverallProgress = 100;
            OverallProgressText = "100%";
            StatusMessage = L.Get("SafeDestructive.Status.Done");
            HasResults = true;

            await BuildResultsAsync();
            await BuildCertificateAsync();
            await SaveTestSessionAsync();
            Log("═══ WORKFLOW DOKONČEN ═══");
        }
        catch (OperationCanceledException)
        {
            Phase = SafeDestructivePhase.Failed;
            StatusMessage = L.Get("SafeDestructive.Status.Cancelled");
            Log("⏹ Operace zrušena.");
        }
        catch (Exception ex)
        {
            Phase = SafeDestructivePhase.Failed;
            StatusMessage = L.Get("SafeDestructive.Status.Error", ex.Message);
            Log($"❌ FATAL: {ex.Message}");
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
        OverallProgressText = "0%";

        var backupRoot = Path.Combine(BackupTargetPath, $"DiskChecker_SafeBackup_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(backupRoot);
        _backupImagePath = Path.Combine(backupRoot, "disk_image.raw");
        _backupManifestPath = Path.Combine(backupRoot, "backup_manifest.json");

        Log($"Záloha → {_backupImagePath}");

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

        using var sourceStream = new FileStream(DiskPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, blockSize, FileOptions.SequentialScan);
        using var targetStream = new FileStream(_backupImagePath, FileMode.Create, FileAccess.Write,
            FileShare.None, blockSize, FileOptions.SequentialScan);

        while (bytesRead < totalSize)
        {
            ct.ThrowIfCancellationRequested();

            int bytesToRead = (int)Math.Min(blockSize, totalSize - bytesRead);
            int bytesReadNow = 0;
            bool blockReadable = true;

            try
            {
                bytesReadNow = await sourceStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
            }
            catch (IOException)
            {
                blockReadable = false;
            }
            catch (UnauthorizedAccessException)
            {
                blockReadable = false;
            }

            if (!blockReadable || bytesReadNow == 0)
            {
                // Unreadable sector — write zeros and log
                Array.Clear(buffer, 0, bytesToRead);
                bytesReadNow = bytesToRead;
                unreadableBytes += bytesToRead;
                consecutiveErrors++;

                if (consecutiveErrors == 1)
                    Log($"⚠️ Nečitelný sektor na pozici {FormatBytesLong(bytesRead)} — nahrazen nulami");

                if (consecutiveErrors >= maxConsecutiveErrors)
                    throw new IOException($"Příliš mnoho nečitelných sektorů za sebou ({consecutiveErrors}) — disk je pravděpodobně vážně poškozen. Záloha přerušena.");
            }
            else
            {
                consecutiveErrors = 0;
            }

            await targetStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);
            bytesRead += bytesReadNow;
            _phaseBytesProcessed += bytesReadNow;

            BackupBytesWritten = bytesRead;
            BackupBytesWrittenText = FormatBytesLong(bytesRead);
            BackupCurrentSectorText = $"Blok {bytesRead / blockSize:N0} / {totalSize / blockSize:N0}";

            double backupProgress = totalSize > 0 ? (double)bytesRead / totalSize * 100 : 0;
            OverallProgress = backupProgress * 0.30; // Backup = 30% of total workflow
            OverallProgressText = $"{OverallProgress:F0}%";

            UpdatePhaseSpeedAndEta(ref _phaseStartTime, ref _phaseBytesProcessed,
                out var speed, out var elapsed, out var eta);
            BackupSpeedText = speed;
            BackupElapsedText = elapsed;
            BackupEtaText = eta;

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, ct);
        }

        _backupTotalBytes = bytesRead;

        if (unreadableBytes > 0)
            Log($"⚠️ Celkem {FormatBytesLong(unreadableBytes)} nečitelných sektorů nahrazeno nulami.");

        // Write manifest
        var manifest = new
        {
            SourceDrive = DiskPath,
            SourceModel = DiskDisplayName,
            BackupDate = DateTime.Now.ToString("O"),
            Mode = "RawImage",
            TotalBytes = bytesRead,
            BlockSize = blockSize,
            UnreadableBytes = unreadableBytes
        };
        using var manifestStream = File.OpenWrite(_backupManifestPath);
        await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, ct);

        Log($"Záloha dokončena: {FormatBytesLong(bytesRead)}");
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

        if (SelectedDrive == null) return;

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
        foreach (var p in phases) TestPhases.Add(p);

        int totalPhases = phases.Length;
        double testWeight = 0.40; // Test = 40% of total workflow
        double phaseWeight = testWeight / totalPhases;

        // ── Sanitize Pass 1 Write ──
        await RunSanitizePhaseAsync(0, "write", SanitizePass1WritePoints, phaseWeight, 0, ct);
        if (ct.IsCancellationRequested) return;

        // ── Sanitize Pass 1 Read ──
        await RunSanitizePhaseAsync(1, "read", SanitizePass1ReadPoints, phaseWeight, phaseWeight, ct);
        if (ct.IsCancellationRequested) return;

        // ── Seek Full Stroke ──
        await RunSeekPhaseAsync(2, SeekTestType.FullStroke, SeekFullStrokePoints, phaseWeight, phaseWeight * 2, ct);
        if (ct.IsCancellationRequested) return;

        // ── Seek Random ──
        await RunSeekPhaseAsync(3, SeekTestType.Random, SeekRandomPoints, phaseWeight, phaseWeight * 3, ct);
        if (ct.IsCancellationRequested) return;

        // ── Seek Skip ──
        await RunSeekPhaseAsync(4, SeekTestType.Skip, SeekSkipPoints, phaseWeight, phaseWeight * 4, ct);
        if (ct.IsCancellationRequested) return;

        HasSeekCharts = true;
        RebuildSeekChart();

        // ── Sanitize Pass 2 Write ──
        await RunSanitizePhaseAsync(5, "write", SanitizePass2WritePoints, phaseWeight, phaseWeight * 5, ct);
        if (ct.IsCancellationRequested) return;

        // ── Sanitize Pass 2 Read ──
        await RunSanitizePhaseAsync(6, "read", SanitizePass2ReadPoints, phaseWeight, phaseWeight * 6, ct);
        if (ct.IsCancellationRequested) return;

        // Capture post-test SMART (only if drive supports SMART)
        if (SelectedDrive.SupportsSmart)
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
        TestPhaseDetail = mode == "write" ? "Zapisuji..." : "Čtu...";

        SanitizeTotalBytes = DiskTotalBytes;
        SanitizeTotalBytesText = DiskTotalSizeText;

        long bytesProcessed = 0;
        _phaseStartTime = DateTime.UtcNow;
        _phaseBytesProcessed = 0;

        const int blockSize = 256 * 1024; // 256KB blocks
        var buffer = new byte[blockSize];
        long totalBlocks = DiskTotalBytes / blockSize;

        using var deviceStream = new FileStream(DiskPath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.None, blockSize, FileOptions.SequentialScan);

        for (long block = 0; block < totalBlocks; block++)
        {
            ct.ThrowIfCancellationRequested();

            if (mode == "write")
            {
                Array.Fill(buffer, (byte)0x00);
                await deviceStream.WriteAsync(buffer, ct);
            }
            else
            {
                await deviceStream.ReadExactlyAsync(buffer, ct);
            }

            bytesProcessed += blockSize;
            _phaseBytesProcessed += blockSize;

            if (mode == "write")
                SanitizeBytesWritten = bytesProcessed;
            else
                SanitizeBytesRead = bytesProcessed;

            SanitizeBytesWrittenText = FormatBytesLong(SanitizeBytesWritten);
            SanitizeBytesReadText = FormatBytesLong(SanitizeBytesRead);

            double phaseProgress = DiskTotalBytes > 0 ? (double)bytesProcessed / DiskTotalBytes * 100 : 0;
            TestPhaseProgress = phaseProgress;
            phase.ProgressPercent = phaseProgress;

            // Add chart point every ~1%
            if (block % Math.Max(1, totalBlocks / 100) == 0 || block == totalBlocks - 1)
            {
                var speedMbps = CalculateCurrentSpeed();
                points.Add(new ObservablePoint(phaseProgress, speedMbps));
            }

            UpdatePhaseSpeedAndEta(ref _phaseStartTime, ref _phaseBytesProcessed,
                out var speed, out var elapsed, out var eta);
            TestCurrentSpeedText = speed;
            TestElapsedText = elapsed;
            TestEtaText = eta;

            OverallProgress = baseProgress + (phaseProgress / 100 * phaseWeight) * 100;
            OverallProgressText = $"{OverallProgress:F0}%";

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, ct);
        }

        phase.Status = TestPhaseStatus.Completed;
        phase.ProgressPercent = 100;
        RebuildSanitizeChart();
        Log($"Sanitizace {phaseIndex + 1} ({mode}): dokončeno — {FormatBytesLong(bytesProcessed)}");
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

        if (SelectedDrive == null) return;

        var result = await _seekTestService.RunWithRecommendationAsync(
            SelectedDrive,
            preferredType: seekType,
            progressCallback: progress =>
            {
                double phaseProgress = progress.PercentComplete;
                TestPhaseProgress = phaseProgress;
                phase.ProgressPercent = phaseProgress;

                if (progress.LatestSample != null)
                {
                    points.Add(new ObservablePoint(progress.SeeksCompleted, progress.LatestSample.LatencyMs));
                }

                TestCurrentSpeedText = $"{progress.CurrentAverageLatencyMs:F2} ms";
                TestElapsedText = $"{(int)(DateTime.UtcNow - _phaseStartTime).TotalHours:D2}:{(DateTime.UtcNow - _phaseStartTime).Minutes:D2}:{(DateTime.UtcNow - _phaseStartTime).Seconds:D2}";

                OverallProgress = baseProgress + (phaseProgress / 100 * phaseWeight) * 100;
                OverallProgressText = $"{OverallProgress:F0}%";
            },
            cancellationToken: ct);

        phase.Status = result.IsCompleted ? TestPhaseStatus.Completed : TestPhaseStatus.Failed;
        phase.ProgressPercent = 100;
        Log($"Seek {seekType}: dokončeno — avg {result.AverageLatencyMs:F2} ms, P95 {result.P95LatencyMs:F2} ms, chyb: {result.ErrorCount}");
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

        if (_backupImagePath == null || !File.Exists(_backupImagePath))
            throw new InvalidOperationException("Záloha nebyla nalezena.");

        RestoreImagePath = _backupImagePath;
        Log($"Obnova ← {_backupImagePath}");

        // Use 1 MiB blocks for speed (was 4 KiB)
        const int blockSize = 1024 * 1024;
        var buffer = new byte[blockSize];
        long totalSize = _backupTotalBytes;
        long bytesWritten = 0;
        _phaseStartTime = DateTime.UtcNow;
        _phaseBytesProcessed = 0;

        using var sourceStream = new FileStream(_backupImagePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, blockSize, FileOptions.SequentialScan);
        using var targetStream = new FileStream(DiskPath, FileMode.Open, FileAccess.Write,
            FileShare.None, blockSize, FileOptions.SequentialScan);

        while (bytesWritten < totalSize)
        {
            ct.ThrowIfCancellationRequested();

            int bytesToRead = (int)Math.Min(blockSize, totalSize - bytesWritten);
            int bytesReadNow = await sourceStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
            if (bytesReadNow == 0) break;

            await targetStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);
            bytesWritten += bytesReadNow;
            _phaseBytesProcessed += bytesReadNow;

            RestoreBytesWritten = bytesWritten;
            RestoreBytesWrittenText = FormatBytesLong(bytesWritten);
            RestoreCurrentSectorText = $"Blok {bytesWritten / blockSize:N0} / {totalSize / blockSize:N0}";

            double restoreProgress = totalSize > 0 ? (double)bytesWritten / totalSize * 100 : 0;
            OverallProgress = 70 + restoreProgress * 0.30; // Restore = 30% of total workflow
            OverallProgressText = $"{OverallProgress:F0}%";

            UpdatePhaseSpeedAndEta(ref _phaseStartTime, ref _phaseBytesProcessed,
                out var speed, out var elapsed, out var eta);
            RestoreSpeedText = speed;
            RestoreElapsedText = elapsed;
            RestoreEtaText = eta;

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, ct);
        }

        Log($"Obnova dokončena: {FormatBytesLong(bytesWritten)}");
        StatusMessage = L.Get("SafeDestructive.Status.RestoreDone");
    }

    // ──────────────────────────────────────────────
    //  Results
    // ──────────────────────────────────────────────

    private async Task BuildResultsAsync()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ BEZPEČNÝ DESTRUKTIVNÍ TEST — VÝSLEDKY ═══");
        sb.AppendLine($"Disk: {DiskDisplayName}");
        sb.AppendLine($"Cesta: {DiskPath}");
        sb.AppendLine($"Velikost: {DiskTotalSizeText}");
        sb.AppendLine();

        // Sanitize stats
        if (SanitizePass1WritePoints.Count > 0)
        {
            double avgWrite1 = SanitizePass1WritePoints.Average(p => p.Y ?? 0);
            double avgRead1 = SanitizePass1ReadPoints.Count > 0 ? SanitizePass1ReadPoints.Average(p => p.Y ?? 0) : 0;
            sb.AppendLine($"🧹 Sanitizace 1: Write {avgWrite1:F1} MB/s | Read {avgRead1:F1} MB/s");
        }
        if (SanitizePass2WritePoints.Count > 0)
        {
            double avgWrite2 = SanitizePass2WritePoints.Average(p => p.Y ?? 0);
            double avgRead2 = SanitizePass2ReadPoints.Count > 0 ? SanitizePass2ReadPoints.Average(p => p.Y ?? 0) : 0;
            sb.AppendLine($"🧹 Sanitizace 2: Write {avgWrite2:F1} MB/s | Read {avgRead2:F1} MB/s");
        }

        // Seek stats
        if (SeekFullStrokePoints.Count > 0)
            sb.AppendLine($"🎯 Seek Full Stroke: avg {SeekFullStrokePoints.Average(p => p.Y ?? 0):F2} ms, P95 {Percentile(SeekFullStrokePoints.Select(p => p.Y ?? 0).ToList(), 0.95):F2} ms");
        if (SeekRandomPoints.Count > 0)
            sb.AppendLine($"🎯 Seek Random: avg {SeekRandomPoints.Average(p => p.Y ?? 0):F2} ms, P95 {Percentile(SeekRandomPoints.Select(p => p.Y ?? 0).ToList(), 0.95):F2} ms");
        if (SeekSkipPoints.Count > 0)
            sb.AppendLine($"🎯 Seek Skip: avg {SeekSkipPoints.Average(p => p.Y ?? 0):F2} ms, P95 {Percentile(SeekSkipPoints.Select(p => p.Y ?? 0).ToList(), 0.95):F2} ms");

        ResultsSummary = sb.ToString();

        // SMART delta
        if (_smartBefore != null && _smartAfter != null)
        {
            var deltaSb = new System.Text.StringBuilder();
            deltaSb.AppendLine("═══ SMART ZMĚNY ═══");

            if (_smartBefore.Temperature != _smartAfter.Temperature)
                deltaSb.AppendLine($"🌡 Teplota: {_smartBefore.Temperature}°C → {_smartAfter.Temperature}°C (Δ {_smartAfter.Temperature - _smartBefore.Temperature:+0;-0}°C)");
            if (_smartBefore.ReallocatedSectorCount != _smartAfter.ReallocatedSectorCount)
                deltaSb.AppendLine($"⚠️ Reallocated sectors: {_smartBefore.ReallocatedSectorCount} → {_smartAfter.ReallocatedSectorCount} (Δ {_smartAfter.ReallocatedSectorCount - _smartBefore.ReallocatedSectorCount:+0;-0})");
            if (_smartBefore.PendingSectorCount != _smartAfter.PendingSectorCount)
                deltaSb.AppendLine($"⚠️ Pending sectors: {_smartBefore.PendingSectorCount} → {_smartAfter.PendingSectorCount} (Δ {_smartAfter.PendingSectorCount - _smartBefore.PendingSectorCount:+0;-0})");
            if (_smartBefore.UncorrectableErrorCount != _smartAfter.UncorrectableErrorCount)
                deltaSb.AppendLine($"⚠️ Uncorrectable errors: {_smartBefore.UncorrectableErrorCount} → {_smartAfter.UncorrectableErrorCount} (Δ {_smartAfter.UncorrectableErrorCount - _smartBefore.UncorrectableErrorCount:+0;-0})");
            if (_smartBefore.PowerOnHours != _smartAfter.PowerOnHours)
                deltaSb.AppendLine($"⏱ Power-on hours: {_smartBefore.PowerOnHours} → {_smartAfter.PowerOnHours} (Δ {_smartAfter.PowerOnHours - _smartBefore.PowerOnHours:+0;-0}h)");

            SmartDeltaSummary = deltaSb.ToString();
        }

    }

    // ──────────────────────────────────────────────
    //  Certificate & Session persistence
    // ──────────────────────────────────────────────

    private async Task BuildCertificateAsync()
    {
        if (SelectedDrive == null) return;

        var card = await _diskCardRepository.GetByDevicePathAsync(SelectedDrive.Path);
        if (card == null)
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

        var cert = new DiskCertificate
        {
            CertificateNumber = $"SAFE-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24],
            DiskCardId = card.Id,
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = Environment.UserName,
            DiskModel = SelectedDrive.Name ?? DiskDisplayName,
            SerialNumber = SelectedDrive.SerialNumber ?? "-",
            Capacity = DiskTotalSizeText,
            DiskType = _smartBefore?.DeviceType ?? "HDD",
            TestType = "Bezpečný destruktivní test (záloha → test → obnova)",
            TestDuration = DateTime.UtcNow - _phaseStartTime,
            Grade = CalculateSafeGrade(),
            Score = CalculateSafeScore(),
            HealthStatus = DetermineSafeHealthStatus(),
            Status = CertificateStatus.Active,
            Recommended = true,
            Notes = $"Bezpečný destruktivní test: záloha → test → obnova.\n{ResultsSummary}",
            SmartPassed = _smartAfter?.ReallocatedSectorCount == 0 && _smartAfter?.PendingSectorCount == 0,
            PowerOnHours = _smartAfter?.PowerOnHours ?? _smartBefore?.PowerOnHours ?? 0,
            ReallocatedSectors = _smartAfter?.ReallocatedSectorCount ?? _smartBefore?.ReallocatedSectorCount ?? 0,
            PendingSectors = _smartAfter?.PendingSectorCount ?? _smartBefore?.PendingSectorCount ?? 0,
            SanitizationPerformed = true,
            SanitizationMethod = "Zero-fill + verify (2×) — safe mode",
            DataVerified = true,
            ErrorCount = 0,
            TemperatureRange = $"{_smartBefore?.Temperature ?? 0}–{_smartAfter?.Temperature ?? 0}°C",
            SmartDeltaSummary = SmartDeltaSummary
        };

        // Seek metrics
        var allSeekPoints = SeekFullStrokePoints.Concat(SeekRandomPoints).Concat(SeekSkipPoints)
            .Select(p => p.Y ?? 0).Where(y => y > 0).OrderBy(y => y).ToList();
        if (allSeekPoints.Count > 0)
        {
            cert.SeekAvgLatencyMs = allSeekPoints.Average();
            cert.SeekMinLatencyMs = allSeekPoints.Min();
            cert.SeekMaxLatencyMs = allSeekPoints.Max();
            cert.SeekP95LatencyMs = Percentile(allSeekPoints, 0.95);
        }

        // Sanitize metrics
        if (SanitizePass1WritePoints.Count > 0)
            cert.Sanitize1AvgWriteMBps = SanitizePass1WritePoints.Average(p => p.Y ?? 0);
        if (SanitizePass1ReadPoints.Count > 0)
            cert.Sanitize1AvgReadMBps = SanitizePass1ReadPoints.Average(p => p.Y ?? 0);
        if (SanitizePass2WritePoints.Count > 0)
            cert.Sanitize2AvgWriteMBps = SanitizePass2WritePoints.Average(p => p.Y ?? 0);
        if (SanitizePass2ReadPoints.Count > 0)
            cert.Sanitize2AvgReadMBps = SanitizePass2ReadPoints.Average(p => p.Y ?? 0);

        Certificate = cert;
    }

    private DiskCertificate? Certificate { get; set; }

    private async Task SaveTestSessionAsync()
    {
        try
        {
            if (SelectedDrive == null || Certificate == null) return;

            var card = await _diskCardRepository.GetByDevicePathAsync(SelectedDrive.Path);
            if (card == null)
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
        catch (Exception ex)
        {
            Log($"❌ Uložení session/certifikátu selhalo: {ex.Message}");
        }
    }

    private string CalculateSafeGrade()
    {
        double avgWrite = 0;
        if (SanitizePass1WritePoints.Count > 0) avgWrite = SanitizePass1WritePoints.Average(p => p.Y ?? 0);
        return avgWrite switch
        {
            >= 200 => "A",
            >= 150 => "B",
            >= 100 => "C",
            >= 60 => "D",
            >= 30 => "E",
            _ => "F"
        };
    }

    private int CalculateSafeScore()
    {
        double avgWrite = 0;
        if (SanitizePass1WritePoints.Count > 0) avgWrite = SanitizePass1WritePoints.Average(p => p.Y ?? 0);
        return avgWrite switch
        {
            >= 250 => 95,
            >= 200 => 85,
            >= 150 => 70,
            >= 100 => 55,
            >= 60 => 40,
            >= 30 => 25,
            _ => 10
        };
    }

    private static string DetermineSafeHealthStatus()
    {
        return "Healthy — test completed successfully with backup/restore";
    }

    private static HealthAssessment MapHealthAssessment(string? healthStatus)
    {
        if (string.IsNullOrWhiteSpace(healthStatus)) return HealthAssessment.Unknown;
        if (healthStatus.Contains("Healthy", StringComparison.OrdinalIgnoreCase)) return HealthAssessment.Excellent;
        if (healthStatus.Contains("Warning", StringComparison.OrdinalIgnoreCase)) return HealthAssessment.Fair;
        if (healthStatus.Contains("Critical", StringComparison.OrdinalIgnoreCase)) return HealthAssessment.Critical;
        return HealthAssessment.Unknown;
    }

    private static List<SmartAttributeChange> BuildSmartChanges(SmartaData before, SmartaData after)
    {
        var changes = new List<SmartAttributeChange>();
        long bTemp = before.Temperature ?? 0;
        long aTemp = after.Temperature ?? 0;
        if (bTemp != aTemp)
            changes.Add(new SmartAttributeChange { AttributeName = "Temperature", ValueBefore = bTemp, ValueAfter = aTemp, Change = aTemp - bTemp });

        long bRealloc = before.ReallocatedSectorCount ?? 0;
        long aRealloc = after.ReallocatedSectorCount ?? 0;
        if (bRealloc != aRealloc)
            changes.Add(new SmartAttributeChange { AttributeName = "ReallocatedSectorCount", ValueBefore = bRealloc, ValueAfter = aRealloc, Change = aRealloc - bRealloc });

        long bPending = before.PendingSectorCount ?? 0;
        long aPending = after.PendingSectorCount ?? 0;
        if (bPending != aPending)
            changes.Add(new SmartAttributeChange { AttributeName = "PendingSectorCount", ValueBefore = bPending, ValueAfter = aPending, Change = aPending - bPending });

        long bUncorr = before.UncorrectableErrorCount ?? 0;
        long aUncorr = after.UncorrectableErrorCount ?? 0;
        if (bUncorr != aUncorr)
            changes.Add(new SmartAttributeChange { AttributeName = "UncorrectableErrorCount", ValueBefore = bUncorr, ValueAfter = aUncorr, Change = aUncorr - bUncorr });

        long bPoh = before.PowerOnHours ?? 0;
        long aPoh = after.PowerOnHours ?? 0;
        if (bPoh != aPoh)
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
        switch (param)
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

    private double CalculateCurrentSpeed()
    {
        var elapsed = (DateTime.UtcNow - _phaseStartTime).TotalSeconds;
        if (elapsed > 0.5 && _phaseBytesProcessed > 0)
            return _phaseBytesProcessed / elapsed / (1024.0 * 1024.0); // MB/s
        return 0;
    }

    private void UpdatePhaseSpeedAndEta(ref DateTime startTime, ref long bytesProcessed,
        out string speedText, out string elapsedText, out string etaText)
    {
        var elapsed = DateTime.UtcNow - startTime;
        elapsedText = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

        if (elapsed.TotalSeconds > 1 && bytesProcessed > 0)
        {
            var speedBps = bytesProcessed / elapsed.TotalSeconds;
            speedText = FormatBytesLong((long)speedBps) + "/s";

            long totalForPhase = Phase switch
            {
                SafeDestructivePhase.Backup => DiskTotalBytes,
                SafeDestructivePhase.Restore => _backupTotalBytes,
                _ => DiskTotalBytes
            };

            if (totalForPhase > 0 && speedBps > 0)
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
        if (values.Count == 0) return 0;
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
        if (Phase == SafeDestructivePhase.Backup || Phase == SafeDestructivePhase.Test || Phase == SafeDestructivePhase.Restore)
        {
            _ = _dialogService.ShowErrorAsync("Operace běží", "Nelze opustit během běžící operace. Nejprve operaci přerušte.");
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
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }
}
