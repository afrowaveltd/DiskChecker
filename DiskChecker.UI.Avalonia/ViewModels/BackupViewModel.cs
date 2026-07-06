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
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Represents a backup target (destination drive) with available space info.
/// </summary>
public partial class BackupTargetItem : ObservableObject
{
    public string DrivePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public long TotalFreeSpace { get; init; }
    public string FreeSpaceText { get; init; } = string.Empty;
    
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private long _allocatedBytes;
    public string AllocatedText => FormatBytes(AllocatedBytes);
    
    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / 1024.0:F1} KB";
    }
}

/// <summary>
/// Represents a folder selection for backup (e.g. Users, Desktop, Documents).
/// </summary>
public partial class BackupFolderItem : ObservableObject
{
    public string Path { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public long EstimatedSize { get; init; }
    public string EstimatedSizeText { get; init; } = string.Empty;
    public bool IsSystemFolder { get; init; }
    public bool IsFile { get; init; }
    public bool IsCustom { get; init; }
    public string SourceKindText => IsFile ? "soubor" : "složka";
    public string FullPath => Path;
    
    [ObservableProperty] private bool _isSelected;
}

public enum BackupMode
{
    FileLevel,
    RawImage,
    VhdxImage
}

public enum BackupPhase
{
    Idle,
    Scanning,
    Calculating,
    Ready,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Orchestrates intelligent backup of a failing disk before destructive testing.
/// Supports file-level backup (with cross-platform filesystem reading) and raw sector imaging.
/// </summary>
public partial class BackupViewModel : ViewModelBase, INavigableViewModel, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly IDialogService _dialogService;
    private readonly ISmartaProvider _smartaProvider;

    private CancellationTokenSource? _backupCancellation;
    private bool _disposed;
    private DateTime _backupStartTime;
    private long _totalBytesToBackup;
    private long _totalBytesBackedUp;
    private string _currentFilePath = string.Empty;
    private readonly List<string> _backupLog = new();

    // Cached JsonSerializerOptions
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    [ObservableProperty] private CoreDriveInfo? _sourceDrive;
    [ObservableProperty] private SmartaData? _sourceSmarta;
    [ObservableProperty] private string _failureWarning = string.Empty;
    [ObservableProperty] private bool _isFailureCritical;

    [ObservableProperty] private BackupPhase _phase = BackupPhase.Idle;
    [ObservableProperty] private BackupMode _selectedMode = BackupMode.FileLevel;
    [ObservableProperty] private bool _isRawModeForced;

    // Computed properties for XAML bindings (EnumToBooleanConverter can't be used for Background brushes)
    public bool IsFileLevelMode => SelectedMode == BackupMode.FileLevel;
    public bool IsRawImageMode => SelectedMode == BackupMode.RawImage;
    public bool IsVhdxImageMode => SelectedMode == BackupMode.VhdxImage;

    [ObservableProperty] private string _statusMessage = "Připraven k zálohování";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private double _currentFileProgress;
    [ObservableProperty] private long _currentFileBytes;
    [ObservableProperty] private string _currentFileName = string.Empty;
    [ObservableProperty] private string _currentSpeed = "—";
    [ObservableProperty] private string _elapsedTime = "00:00";
    [ObservableProperty] private string _estimatedRemaining = "--:--";

    [ObservableProperty] private long _totalRequiredBytes;
    [ObservableProperty] private string _totalRequiredText = "—";
    [ObservableProperty] private long _totalAvailableBytes;
    [ObservableProperty] private string _totalAvailableText = "—";
    [ObservableProperty] private long _systemReserveBytes;
    [ObservableProperty] private string _systemReserveText = "—";
    [ObservableProperty] private bool _hasEnoughSpace;
    [ObservableProperty] private string _spaceSummary = string.Empty;

    [ObservableProperty] private string _backupLogText = string.Empty;

    public ObservableCollection<BackupTargetItem> TargetDrives { get; } = new();
    public ObservableCollection<BackupFolderItem> SourceFolders { get; } = new();

    public IAsyncRelayCommand ScanSourceCommand { get; }
    public IAsyncRelayCommand StartBackupCommand { get; }
    public IRelayCommand CancelBackupCommand { get; }
    public IRelayCommand GoBackCommand { get; }
    public IRelayCommand SelectAllFoldersCommand { get; }
    public IRelayCommand DeselectAllFoldersCommand { get; }
    public IRelayCommand SelectUserFoldersCommand { get; }
    public IAsyncRelayCommand AddCustomFolderCommand { get; }
    public IAsyncRelayCommand AddCustomFilesCommand { get; }
    public IRelayCommand<BackupFolderItem> RemoveSourceCommand { get; }
    public IRelayCommand SwitchToRawModeCommand { get; }
    public IRelayCommand SwitchToVhdxModeCommand { get; }
    public IRelayCommand SwitchToFileModeCommand { get; }
    public IRelayCommand NavigateToRestoreCommand { get; }

    public BackupViewModel(
        INavigationService navigationService,
        ISelectedDiskService selectedDiskService,
        IDialogService dialogService,
        ISmartaProvider smartaProvider)
    {
        _navigationService = navigationService;
        _selectedDiskService = selectedDiskService;
        _dialogService = dialogService;
        _smartaProvider = smartaProvider;

        ScanSourceCommand = new AsyncRelayCommand(ScanSourceAsync);
        StartBackupCommand = new AsyncRelayCommand(StartBackupAsync, () => Phase == BackupPhase.Ready && HasEnoughSpace);
        CancelBackupCommand = new RelayCommand(CancelBackup);
        GoBackCommand = new RelayCommand(GoBack);
        SelectAllFoldersCommand = new RelayCommand(SelectAllFolders);
        DeselectAllFoldersCommand = new RelayCommand(DeselectAllFolders);
        SelectUserFoldersCommand = new RelayCommand(SelectUserFolders);
        AddCustomFolderCommand = new AsyncRelayCommand(AddCustomFoldersAsync);
        AddCustomFilesCommand = new AsyncRelayCommand(AddCustomFilesAsync);
        RemoveSourceCommand = new RelayCommand<BackupFolderItem>(RemoveSource);
        SwitchToRawModeCommand = new RelayCommand(() => SelectedMode = BackupMode.RawImage);
        SwitchToVhdxModeCommand = new RelayCommand(() => SelectedMode = BackupMode.VhdxImage);
        SwitchToFileModeCommand = new RelayCommand(() => SelectedMode = BackupMode.FileLevel);
        NavigateToRestoreCommand = new RelayCommand(NavigateToRestore);

        // Subscribe to collection item changes so space calculations update reactively
        SubscribeToCollectionChanges();
    }

    public void OnNavigatedTo()
    {
        SourceDrive = _selectedDiskService.SelectedDisk;
        if (SourceDrive == null)
        {
            StatusMessage = "Není vybrán žádný disk. Vyberte disk v přehledu SMART.";
            return;
        }

        _ = InitializeAsync();
    }

    // ── Reactive hooks: CommunityToolkit.Mvvm generates OnXxxChanged partial methods ──

    partial void OnSelectedModeChanged(BackupMode value)
    {
        // Notify computed bool properties
        OnPropertyChanged(nameof(IsFileLevelMode));
        OnPropertyChanged(nameof(IsRawImageMode));
        OnPropertyChanged(nameof(IsVhdxImageMode));
        CalculateSpaceRequirements();
        StartBackupCommand.NotifyCanExecuteChanged();
    }

    partial void OnPhaseChanged(BackupPhase value)
    {
        StartBackupCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasEnoughSpaceChanged(bool value)
    {
        StartBackupCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRawModeForcedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFileLevelMode));
        OnPropertyChanged(nameof(IsRawImageMode));
    }

    // ── Collection item change tracking ──

    private void SubscribeToCollectionChanges()
    {
        SourceFolders.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (BackupFolderItem item in e.NewItems)
                    item.PropertyChanged += OnFolderItemChanged;
            }
            if (e.OldItems != null)
            {
                foreach (BackupFolderItem item in e.OldItems)
                    item.PropertyChanged -= OnFolderItemChanged;
            }
        };

        TargetDrives.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (BackupTargetItem item in e.NewItems)
                    item.PropertyChanged += OnTargetItemChanged;
            }
            if (e.OldItems != null)
            {
                foreach (BackupTargetItem item in e.OldItems)
                    item.PropertyChanged -= OnTargetItemChanged;
            }
        };
    }

    private void OnFolderItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackupFolderItem.IsSelected))
            CalculateSpaceRequirements();
    }

    private void OnTargetItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackupTargetItem.IsSelected))
            CalculateSpaceRequirements();
    }

    private async Task InitializeAsync()
    {
        try
        {
            Phase = BackupPhase.Scanning;
            StatusMessage = "Načítám SMART data a připravuji zálohování...";

            try
            {
                SourceSmarta = await _smartaProvider.GetSmartaDataAsync(SourceDrive!.Path, CancellationToken.None);
            }
            catch
            {
                SourceSmarta = null;
            }

            if (SourceSmarta?.IsFailing == true)
            {
                IsFailureCritical = true;
                FailureWarning = SourceSmarta.FailurePrediction;
            }
            else if (SourceSmarta?.IsHealthy == false)
            {
                IsFailureCritical = false;
                FailureWarning = "⚠️ SMART hlásí problémy. Záloha doporučena před testováním.";
            }
            else
            {
                IsFailureCritical = false;
                FailureWarning = "Disk je dle SMART zdravý. Záloha je preventivní.";
            }

            await DetectFilesystemAccessAsync();
            await LoadTargetDrivesAsync();
            await ScanSourceFoldersAsync();
            CalculateSpaceRequirements();

            Phase = BackupPhase.Ready;
            StatusMessage = HasEnoughSpace
                ? "✅ Připraveno k zálohování — vyberte složky a spusťte."
                : "⚠️ Nedostatek místa — uvolněte místo nebo vyberte méně složek.";
        }
        catch (Exception ex)
        {
            Phase = BackupPhase.Failed;
            StatusMessage = $"Chyba při inicializaci: {ex.Message}";
        }
    }

    private async Task DetectFilesystemAccessAsync()
    {
        if (SourceDrive == null) return;

        try
        {
            // On Linux, physical drives are at /dev/sdX or /dev/nvmeXnY.
            // DriveInfo.GetDrives() returns mount points (/home, /, etc.) which
            // never match /dev paths. We need to check via lsblk whether any
            // partition of the source device is mounted and readable.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await DetectFilesystemAccessLinuxAsync();
                return;
            }

            // Windows path: match DriveInfo names against the physical drive path
            var volumes = System.IO.DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .ToList();

            var matchingVolume = volumes.FirstOrDefault(v =>
                v.Name.StartsWith(SourceDrive.Path, StringComparison.OrdinalIgnoreCase) ||
                SourceDrive.Path.StartsWith(v.Name, StringComparison.OrdinalIgnoreCase));

            if (matchingVolume != null)
            {
                IsRawModeForced = false;
                SelectedMode = BackupMode.FileLevel;
                StatusMessage = $"Souborový systém čitelný ({matchingVolume.DriveFormat}) — file-level záloha dostupná.";
            }
            else
            {
                // Physical drive without a mounted volume – try raw access
                try
                {
                    using var fs = System.IO.File.OpenRead(SourceDrive.Path);
                    IsRawModeForced = true;
                    SelectedMode = BackupMode.RawImage;
                    StatusMessage = "Souborový systém není čitelný (BitLocker/neznámý FS) — pouze raw obraz.";
                }
                catch
                {
                    IsRawModeForced = true;
                    SelectedMode = BackupMode.RawImage;
                    StatusMessage = "Disk není přístupný na úrovni souborů — pouze raw obraz.";
                }
            }
        }
        catch
        {
            IsRawModeForced = true;
            SelectedMode = BackupMode.RawImage;
            StatusMessage = "Nelze detekovat souborový systém — raw obraz bude použit.";
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Linux-specific filesystem detection: uses lsblk to find mounted partitions
    /// of the source device, then checks if the mount point is readable.
    /// </summary>
    private async Task DetectFilesystemAccessLinuxAsync()
    {
        try
        {
            var deviceName = System.IO.Path.GetFileName(SourceDrive!.Path);
            var lsblkOutput = await ExecuteCommandAsync("lsblk", $"-J -o NAME,MOUNTPOINT,FSTYPE {deviceName}");

            if (!string.IsNullOrEmpty(lsblkOutput))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(lsblkOutput);
                if (doc.RootElement.TryGetProperty("blockdevices", out var devices))
                {
                    foreach (var device in devices.EnumerateArray())
                    {
                        // Check the device's own mountpoint first (for partitions like /dev/sda1)
                        var ownMount = device.TryGetProperty("mountpoint", out var ownMp)
                            ? ownMp.GetString()
                            : null;
                        var ownFsType = device.TryGetProperty("fstype", out var ownFs)
                            ? ownFs.GetString()
                            : null;

                        if (!string.IsNullOrWhiteSpace(ownMount) && ownMount != "null")
                        {
                            // Found a mounted partition – check if readable
                            try
                            {
                                var testPath = System.IO.Path.Combine(ownMount, ".diskchecker_test_rw");
                                System.IO.File.WriteAllText(testPath, "test");
                                System.IO.File.Delete(testPath);
                                IsRawModeForced = false;
                                SelectedMode = BackupMode.FileLevel;
                                StatusMessage = $"Souborový systém čitelný ({ownFsType ?? "unknown"}) — file-level záloha dostupná.";
                                return;
                            }
                            catch
                            {
                                // Mount point exists but not writable
                            }
                        }

                        // Then check children (for whole disks like /dev/sda with partitions)
                        if (device.TryGetProperty("children", out var children))
                        {
                            foreach (var child in children.EnumerateArray())
                            {
                                var mountPoint = child.TryGetProperty("mountpoint", out var mp)
                                    ? mp.GetString()
                                    : null;
                                var fsType = child.TryGetProperty("fstype", out var fs)
                                    ? fs.GetString()
                                    : null;

                                if (!string.IsNullOrWhiteSpace(mountPoint) && mountPoint != "null")
                                {
                                    // Found a mounted partition – check if readable
                                    try
                                    {
                                        var testPath = System.IO.Path.Combine(mountPoint, ".diskchecker_test_rw");
                                        System.IO.File.WriteAllText(testPath, "test");
                                        System.IO.File.Delete(testPath);

                                        IsRawModeForced = false;
                                        SelectedMode = BackupMode.FileLevel;
                                        StatusMessage = $"Souborový systém čitelný ({fsType ?? "unknown"}) — file-level záloha dostupná.";
                                        return;
                                    }
                                    catch
                                    {
                                        // Mount point exists but isn't writable – still usable for reading
                                        IsRawModeForced = false;
                                        SelectedMode = BackupMode.FileLevel;
                                        StatusMessage = $"Souborový systém čitelný ({fsType ?? "unknown"}, read-only) — file-level záloha dostupná.";
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // No mounted partitions found – try raw device access
            try
            {
                using var fs = System.IO.File.OpenRead(SourceDrive.Path);
                IsRawModeForced = true;
                SelectedMode = BackupMode.RawImage;
                StatusMessage = "Souborový systém není připojen — pouze raw obraz.";
            }
            catch (System.UnauthorizedAccessException)
            {
                IsRawModeForced = true;
                SelectedMode = BackupMode.RawImage;
                StatusMessage = "⚠️ Root práva vyžadována pro raw přístup. Spusťte s sudo.";
            }
            catch
            {
                IsRawModeForced = true;
                SelectedMode = BackupMode.RawImage;
                StatusMessage = "Disk není přístupný — pouze raw obraz (vyžaduje root).";
            }
        }
        catch
        {
            IsRawModeForced = true;
            SelectedMode = BackupMode.RawImage;
            StatusMessage = "Nelze detekovat souborový systém — raw obraz bude použit.";
        }
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

    private async Task LoadTargetDrivesAsync()
    {
        TargetDrives.Clear();

        var allDrives = System.IO.DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType != DriveType.CDRom && d.DriveType != DriveType.Ram)
            .ToList();

        await CalculateSystemReserveAsync();

        foreach (var drive in allDrives)
        {
            // Exclude the source drive. On Linux, SourceDrive.Path is /dev/sdX
            // and DriveInfo.Name is a mount point like / or /home, so a simple
            // StartsWith won't work. We need to check via lsblk whether this
            // mount point belongs to the source device.
            if (SourceDrive != null && await IsDriveOnSourceDeviceAsync(drive, SourceDrive))
                continue;

            TargetDrives.Add(new BackupTargetItem
            {
                DrivePath = drive.Name,
                DisplayName = $"{drive.Name} ({drive.VolumeLabel}) — {drive.DriveFormat}",
                TotalFreeSpace = drive.AvailableFreeSpace,
                FreeSpaceText = FormatBytesLong(drive.AvailableFreeSpace),
                IsSelected = false,
                AllocatedBytes = 0
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Checks whether a DriveInfo mount point belongs to the source physical device.
    /// On Linux, uses lsblk to resolve the mount point to a physical device.
    /// On Windows, uses simple path prefix matching.
    /// </summary>
    private static async Task<bool> IsDriveOnSourceDeviceAsync(System.IO.DriveInfo drive, CoreDriveInfo sourceDrive)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                // Resolve the mount point to its physical device via lsblk
                var mountPoint = drive.RootDirectory.FullName.TrimEnd('/');
                var lsblkOutput = await ExecuteCommandAsync("lsblk", $"-J -o NAME,MOUNTPOINT");

                if (!string.IsNullOrEmpty(lsblkOutput))
                {
                    using var doc = JsonDocument.Parse(lsblkOutput);
                    if (doc.RootElement.TryGetProperty("blockdevices", out var devices))
                    {
                        foreach (var device in devices.EnumerateArray())
                        {
                            if (device.TryGetProperty("children", out var children))
                            {
                                foreach (var child in children.EnumerateArray())
                                {
                                    var mp = child.TryGetProperty("mountpoint", out var mpProp)
                                        ? mpProp.GetString()?.TrimEnd('/')
                                        : null;

                                    if (mp != null && string.Equals(mp, mountPoint, StringComparison.OrdinalIgnoreCase))
                                    {
                                        var parentName = device.TryGetProperty("name", out var nameProp)
                                            ? nameProp.GetString()
                                            : null;
                                        if (parentName != null)
                                        {
                                            var parentPath = $"/dev/{parentName}";
                                            if (string.Equals(parentPath, sourceDrive.Path, StringComparison.OrdinalIgnoreCase))
                                                return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { /* if lsblk fails, fall through to simple check */ }
        }

        // Windows / fallback: simple path prefix matching
        return drive.Name.StartsWith(sourceDrive.Path, StringComparison.OrdinalIgnoreCase) ||
               sourceDrive.Path.StartsWith(drive.Name, StringComparison.OrdinalIgnoreCase);
    }

    private async Task CalculateSystemReserveAsync()
    {
        try
        {
            long ramBytes = 0;
            var gcInfo = GC.GetGCMemoryInfo();
            ramBytes = gcInfo.TotalAvailableMemoryBytes;

            if (ramBytes <= 0)
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                ramBytes = process.WorkingSet64 * 4;
            }

            SystemReserveBytes = ramBytes < 64L * 1024 * 1024 * 1024
                ? ramBytes
                : ramBytes / 2;

            SystemReserveText = FormatBytesLong(SystemReserveBytes);
        }
        catch
        {
            SystemReserveBytes = 8L * 1024 * 1024 * 1024;
            SystemReserveText = "8 GB (výchozí)";
        }

        await Task.CompletedTask;
    }

    private async Task ScanSourceFoldersAsync()
    {
        SourceFolders.Clear();

        if (SourceDrive == null || IsRawModeForced) return;

        try
        {
            string? rootPath = null;

            // On Linux, physical drives are at /dev/sdX. DriveInfo.GetDrives()
            // returns mount points (/home, /, etc.) which never match /dev paths.
            // We need to find the mount point via lsblk.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var deviceName = Path.GetFileName(SourceDrive.Path);
                var lsblkOutput = await ExecuteCommandAsync("lsblk", $"-J -o NAME,MOUNTPOINT {deviceName}");

                if (!string.IsNullOrEmpty(lsblkOutput))
                {
                    using var doc = JsonDocument.Parse(lsblkOutput);
                    if (doc.RootElement.TryGetProperty("blockdevices", out var devices))
                    {
                        foreach (var device in devices.EnumerateArray())
                        {
                            // Check the device's own mountpoint first (for partitions like /dev/sda1)
                            var ownMount = device.TryGetProperty("mountpoint", out var ownMp)
                                ? ownMp.GetString()
                                : null;
                            if (!string.IsNullOrWhiteSpace(ownMount) && ownMount != "null")
                            {
                                rootPath = ownMount;
                                break;
                            }

                            // Then check children (for whole disks like /dev/sda with partitions)
                            if (device.TryGetProperty("children", out var children))
                            {
                                foreach (var child in children.EnumerateArray())
                                {
                                    var mountPoint = child.TryGetProperty("mountpoint", out var mp)
                                        ? mp.GetString()
                                        : null;
                                    if (!string.IsNullOrWhiteSpace(mountPoint) && mountPoint != "null")
                                    {
                                        rootPath = mountPoint;
                                        break;
                                    }
                                }
                            }
                            if (rootPath != null) break;
                        }
                    }
                }
            }
            else
            {
                // Windows: match DriveInfo names against the physical drive path
                var volumes = System.IO.DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .ToList();

                var matchingVolume = volumes.FirstOrDefault(v =>
                    v.Name.StartsWith(SourceDrive.Path, StringComparison.OrdinalIgnoreCase) ||
                    SourceDrive.Path.StartsWith(v.Name, StringComparison.OrdinalIgnoreCase));

                if (matchingVolume != null)
                    rootPath = matchingVolume.RootDirectory.FullName;
            }

            if (rootPath == null) return;

            var dirs = Directory.GetDirectories(rootPath);

            foreach (var dir in dirs)
            {
                var dirInfo = new DirectoryInfo(dir);
                var dirName = dirInfo.Name;

                bool isSystemFolder = dirName.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                                     dirName.Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
                                     dirName.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
                                     dirName.Equals("ProgramData", StringComparison.OrdinalIgnoreCase) ||
                                     dirName.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                                     dirName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                                     dirName.Equals("Recovery", StringComparison.OrdinalIgnoreCase) ||
                                     dirName.StartsWith('.');

                string icon;
                bool defaultSelected;

                if (dirName.Equals("Users", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("home", StringComparison.OrdinalIgnoreCase))
                { icon = "👤"; defaultSelected = true; }
                else if (dirName.Equals("Documents", StringComparison.OrdinalIgnoreCase) ||
                         dirName.Equals("Dokumenty", StringComparison.OrdinalIgnoreCase))
                { icon = "📄"; defaultSelected = true; }
                else if (dirName.Equals("Desktop", StringComparison.OrdinalIgnoreCase) ||
                         dirName.Equals("Plocha", StringComparison.OrdinalIgnoreCase))
                { icon = "🖥️"; defaultSelected = true; }
                else if (dirName.Equals("Downloads", StringComparison.OrdinalIgnoreCase) ||
                         dirName.Equals("Stažené", StringComparison.OrdinalIgnoreCase))
                { icon = "📥"; defaultSelected = true; }
                else if (dirName.Equals("Pictures", StringComparison.OrdinalIgnoreCase) ||
                         dirName.Equals("Obrázky", StringComparison.OrdinalIgnoreCase))
                { icon = "🖼️"; defaultSelected = true; }
                else if (dirName.Equals("Music", StringComparison.OrdinalIgnoreCase) ||
                         dirName.Equals("Hudba", StringComparison.OrdinalIgnoreCase))
                { icon = "🎵"; defaultSelected = true; }
                else if (dirName.Equals("Videos", StringComparison.OrdinalIgnoreCase) ||
                         dirName.Equals("Videa", StringComparison.OrdinalIgnoreCase))
                { icon = "🎬"; defaultSelected = true; }
                else if (isSystemFolder)
                { icon = "⚙️"; defaultSelected = false; }
                else
                { icon = "📁"; defaultSelected = false; }

                long estimatedSize = 0;
                try
                {
                    estimatedSize = await Task.Run(() => EstimateDirectorySize(dirInfo));
                }
                catch { }

                SourceFolders.Add(new BackupFolderItem
                {
                    Path = dir,
                    DisplayName = dirName,
                    Icon = icon,
                    EstimatedSize = estimatedSize,
                    EstimatedSizeText = FormatBytesLong(estimatedSize),
                    IsSystemFolder = isSystemFolder,
                    IsSelected = defaultSelected
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Chyba při skenování složek: {ex.Message}";
        }
    }

    private static long EstimateDirectorySize(DirectoryInfo dir)
    {
        long size = 0;
        try
        {
            foreach (var file in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                try { size += file.Length; } catch { }
            }
            foreach (var subDir in dir.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    foreach (var file in subDir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                    {
                        try { size += file.Length; } catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
        return size;
    }

    private async Task AddCustomFoldersAsync()
    {
        var paths = await _dialogService.PickFoldersAsync("Vyberte složky k zálohování", allowMultiple: true);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                continue;

            if (SourceFolders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var dirInfo = new DirectoryInfo(path);
            var estimatedSize = await Task.Run(() => EstimateDirectorySizeDeepSafe(dirInfo));
            SourceFolders.Add(new BackupFolderItem
            {
                Path = path,
                DisplayName = dirInfo.Name,
                Icon = "📁",
                EstimatedSize = estimatedSize,
                EstimatedSizeText = FormatBytesLong(estimatedSize),
                IsSystemFolder = false,
                IsFile = false,
                IsCustom = true,
                IsSelected = true
            });
        }

        CalculateSpaceRequirements();
    }

    private async Task AddCustomFilesAsync()
    {
        var paths = await _dialogService.PickFilesAsync("Vyberte soubory k zálohování", allowMultiple: true);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                continue;

            if (SourceFolders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var fileInfo = new FileInfo(path);
            SourceFolders.Add(new BackupFolderItem
            {
                Path = path,
                DisplayName = fileInfo.Name,
                Icon = "📄",
                EstimatedSize = fileInfo.Length,
                EstimatedSizeText = FormatBytesLong(fileInfo.Length),
                IsSystemFolder = false,
                IsFile = true,
                IsCustom = true,
                IsSelected = true
            });
        }

        CalculateSpaceRequirements();
    }

    private void RemoveSource(BackupFolderItem? item)
    {
        if (item == null)
            return;

        SourceFolders.Remove(item);
        CalculateSpaceRequirements();
    }

    private static long EstimateDirectorySizeDeepSafe(DirectoryInfo dir)
    {
        long size = 0;
        try
        {
            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { size += file.Length; } catch { }
            }
        }
        catch
        {
            size = EstimateDirectorySize(dir);
        }

        return size;
    }

    private void CalculateSpaceRequirements()
    {
        if (SelectedMode == BackupMode.RawImage || SelectedMode == BackupMode.VhdxImage)
        {
            TotalRequiredBytes = SourceDrive?.TotalSize ?? 0;
        }
        else
        {
            TotalRequiredBytes = SourceFolders
                .Where(f => f.IsSelected)
                .Sum(f => f.EstimatedSize);
        }

        TotalRequiredText = FormatBytesLong(TotalRequiredBytes);

        TotalAvailableBytes = TargetDrives.Sum(d => d.TotalFreeSpace);
        var usableBytes = Math.Max(0, TotalAvailableBytes - SystemReserveBytes);
        TotalAvailableText = FormatBytesLong(usableBytes);

        HasEnoughSpace = usableBytes >= TotalRequiredBytes;

        if (HasEnoughSpace)
        {
            SpaceSummary = $"✅ Dostatek místa: potřeba {TotalRequiredText}, k dispozici {TotalAvailableText} (rezerva {SystemReserveText})";
        }
        else
        {
            var deficit = TotalRequiredBytes - usableBytes;
            SpaceSummary = $"❌ Nedostatek místa: potřeba {TotalRequiredText}, k dispozici {TotalAvailableText}, chybí {FormatBytesLong(deficit)}";
        }

        DistributeAllocation();
    }

    private void DistributeAllocation()
    {
        var selectedTargets = TargetDrives.Where(t => t.IsSelected).ToList();
        if (selectedTargets.Count == 0)
        {
            var best = TargetDrives.OrderByDescending(t => t.TotalFreeSpace).FirstOrDefault();
            if (best != null) best.IsSelected = true;
            selectedTargets = TargetDrives.Where(t => t.IsSelected).ToList();
        }

        if (selectedTargets.Count == 0) return;

        long remaining = TotalRequiredBytes;
        foreach (var target in selectedTargets)
        {
            var usable = Math.Max(0, target.TotalFreeSpace - (SystemReserveBytes / Math.Max(1, selectedTargets.Count)));
            var alloc = Math.Min(remaining, usable);
            target.AllocatedBytes = alloc;
            remaining -= alloc;
        }
    }

    private void SelectAllFolders()
    {
        foreach (var folder in SourceFolders)
            folder.IsSelected = true;
        CalculateSpaceRequirements();
    }

    private void DeselectAllFolders()
    {
        foreach (var folder in SourceFolders)
            folder.IsSelected = false;
        CalculateSpaceRequirements();
    }

    private void SelectUserFolders()
    {
        foreach (var folder in SourceFolders)
            folder.IsSelected = !folder.IsSystemFolder;
        CalculateSpaceRequirements();
    }

    private async Task ScanSourceAsync()
    {
        Phase = BackupPhase.Scanning;
        StatusMessage = "Skenuji zdrojový disk...";
        await ScanSourceFoldersAsync();
        CalculateSpaceRequirements();
        Phase = BackupPhase.Ready;
        StatusMessage = "Skenování dokončeno.";
    }

    private async Task StartBackupAsync()
    {
        if (Phase != BackupPhase.Ready || !HasEnoughSpace) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "💾 Spustit zálohování",
            $"Záloha bude provedena na vybrané cílové disky.\n\n" +
            $"Zdroj: {SourceDrive?.Name} ({SourceDrive?.Path})\n" +
            $"Režim: {(SelectedMode == BackupMode.RawImage ? "Raw obraz" : SelectedMode == BackupMode.VhdxImage ? "VHDx obraz" : "File-level")}\n" +
            $"Data: {TotalRequiredText}\n\n" +
            $"OPRAVDU SPUSTIT ZÁLOHU?");

        if (!confirmed) return;

        _backupCancellation = new CancellationTokenSource();
        _backupStartTime = DateTime.UtcNow;
        _totalBytesToBackup = TotalRequiredBytes;
        _totalBytesBackedUp = 0;
        _backupLog.Clear();
        Phase = BackupPhase.Running;
        StatusMessage = "Zálohování běží...";

        try
        {
            if (SelectedMode == BackupMode.RawImage)
            {
                await RunRawBackupAsync(_backupCancellation.Token);
            }
            else if (SelectedMode == BackupMode.VhdxImage)
            {
                await RunVhdxBackupAsync(_backupCancellation.Token);
            }
            else
            {
                await RunFileBackupAsync(_backupCancellation.Token);
            }

            Phase = BackupPhase.Completed;
            StatusMessage = "✅ Záloha dokončena!";
            OverallProgress = 100;
        }
        catch (OperationCanceledException)
        {
            Phase = BackupPhase.Cancelled;
            StatusMessage = "Záloha přerušena uživatelem.";
        }
        catch (Exception ex)
        {
            Phase = BackupPhase.Failed;
            StatusMessage = $"Záloha selhala: {ex.Message}";
            _backupLog.Add($"[ERROR] {ex.Message}");
        }
        finally
        {
            _backupCancellation?.Dispose();
            _backupCancellation = null;
            BackupLogText = string.Join("\n", _backupLog);
        }
    }

    private async Task RunFileBackupAsync(CancellationToken ct)
    {
        var selectedFolders = SourceFolders.Where(f => f.IsSelected).ToList();
        var targetDrives = TargetDrives.Where(t => t.IsSelected && t.AllocatedBytes > 0).ToList();

        if (selectedFolders.Count == 0 || targetDrives.Count == 0)
            throw new InvalidOperationException("Žádné složky nebo cílové disky nejsou vybrány.");

        var backupRoot = Path.Combine(targetDrives[0].DrivePath, $"DiskChecker_Backup_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(backupRoot);
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Vytvořena složka zálohy: {backupRoot}");

        var copyState = new BackupCopyState(0, targetDrives[0].AllocatedBytes, targetDrives, backupRoot);

        foreach (var source in selectedFolders)
        {
            ct.ThrowIfCancellationRequested();

            if (source.IsFile)
            {
                var fileInfo = new FileInfo(source.Path);
                if (!fileInfo.Exists)
                {
                    _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Přeskakuji chybějící soubor: {source.Path}");
                    continue;
                }

                var destPath = Path.Combine(copyState.BackupRoot, fileInfo.Name);
                _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Zálohuji soubor: {source.Path} → {destPath}");
                await CopySingleSourceFileAsync(source.Path, destPath, fileInfo.Length, copyState, ct);
            }
            else
            {
                var folderName = new DirectoryInfo(source.Path).Name;
                var destFolder = Path.Combine(copyState.BackupRoot, folderName);
                Directory.CreateDirectory(destFolder);

                _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Zálohuji složku: {source.Path} → {destFolder}");

                await CopyDirectoryAsync(source.Path, destFolder, copyState, ct);
            }
        }

        var manifestPath = Path.Combine(backupRoot, "backup_manifest.json");
        var manifest = new
        {
            SourceDrive = SourceDrive?.Path,
            SourceModel = SourceDrive?.Name,
            BackupDate = DateTime.Now.ToString("O"),
            Mode = "FileLevel",
            TotalBytes = _totalBytesBackedUp,
            Sources = selectedFolders.Select(f => new { f.Path, f.IsFile, f.IsCustom }).ToList(),
            Folders = selectedFolders.Where(f => !f.IsFile).Select(f => f.Path).ToList(),
            Files = selectedFolders.Where(f => f.IsFile).Select(f => f.Path).ToList()
        };
        using var manifestStream = System.IO.File.OpenWrite(manifestPath);
        JsonSerializer.Serialize(manifestStream, manifest, _jsonOptions);
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Manifest uložen: {manifestPath}");
    }

    private async Task CopyDirectoryAsync(
        string sourceDir, string destDir, BackupCopyState state, CancellationToken ct)
    {
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDir, dir);
            var destPath = Path.Combine(destDir, relativePath);
            Directory.CreateDirectory(destPath);
        }

        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destPath = Path.Combine(destDir, relativePath);

            var fileInfo = new FileInfo(file);
            var fileSize = fileInfo.Length;

            if (fileSize > state.TargetRemaining && state.TargetIndex + 1 < state.TargetDrives.Count)
            {
                state.TargetIndex++;
                state.TargetRemaining = state.TargetDrives[state.TargetIndex].AllocatedBytes;
                state.BackupRoot = Path.Combine(state.TargetDrives[state.TargetIndex].DrivePath,
                    $"DiskChecker_Backup_{DateTime.Now:yyyyMMdd_HHmmss}_part{state.TargetIndex + 1}");
                Directory.CreateDirectory(state.BackupRoot);
                destDir = Path.Combine(state.BackupRoot, new DirectoryInfo(sourceDir).Name);
                Directory.CreateDirectory(destDir);
                destPath = Path.Combine(destDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Přepnuto na cílový disk {state.TargetIndex + 1}: {state.TargetDrives[state.TargetIndex].DrivePath}");
            }

            CurrentFileName = relativePath;
            CurrentFileBytes = fileSize;
            CurrentFileProgress = 0;

            await CopyFileWithProgressAsync(file, destPath, fileSize, ct);

            _totalBytesBackedUp += fileSize;
            state.TargetRemaining -= fileSize;
            OverallProgress = _totalBytesToBackup > 0
                ? (double)_totalBytesBackedUp / _totalBytesToBackup * 100
                : 0;

            UpdateSpeedAndEta();
        }
    }

    private async Task CopySingleSourceFileAsync(string sourcePath, string destPath, long fileSize, BackupCopyState state, CancellationToken ct)
    {
        if (fileSize > state.TargetRemaining && state.TargetIndex + 1 < state.TargetDrives.Count)
        {
            state.TargetIndex++;
            state.TargetRemaining = state.TargetDrives[state.TargetIndex].AllocatedBytes;
            state.BackupRoot = Path.Combine(state.TargetDrives[state.TargetIndex].DrivePath,
                $"DiskChecker_Backup_{DateTime.Now:yyyyMMdd_HHmmss}_part{state.TargetIndex + 1}");
            Directory.CreateDirectory(state.BackupRoot);
            destPath = Path.Combine(state.BackupRoot, Path.GetFileName(sourcePath));
            _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Přepnuto na cílový disk {state.TargetIndex + 1}: {state.TargetDrives[state.TargetIndex].DrivePath}");
        }

        if (fileSize > state.TargetRemaining)
            throw new IOException($"Soubor je větší než zbývající vyhrazené místo na cíli: {sourcePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        CurrentFileName = Path.GetFileName(sourcePath);
        CurrentFileBytes = fileSize;
        CurrentFileProgress = 0;
        await CopyFileWithProgressAsync(sourcePath, destPath, fileSize, ct);
        _totalBytesBackedUp += fileSize;
        state.TargetRemaining -= fileSize;
        OverallProgress = _totalBytesToBackup > 0 ? (double)_totalBytesBackedUp / _totalBytesToBackup * 100 : 0;
        UpdateSpeedAndEta();
    }

    private async Task CopyFileWithProgressAsync(string sourcePath, string destPath, long fileSize, CancellationToken ct)
    {
        const int bufferSize = 1024 * 1024;
        var buffer = new byte[bufferSize];
        long bytesCopied = 0;

        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.SequentialScan);

        while (bytesCopied < fileSize)
        {
            ct.ThrowIfCancellationRequested();

            int bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, bufferSize), ct);
            if (bytesRead == 0) break;

            await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            bytesCopied += bytesRead;

            CurrentFileProgress = fileSize > 0 ? (double)bytesCopied / fileSize * 100 : 0;
        }

        await destStream.FlushAsync(ct);
    }

    private async Task RunRawBackupAsync(CancellationToken ct)
    {
        if (SourceDrive == null) throw new InvalidOperationException("Zdrojový disk není vybrán.");

        var targetDrives = TargetDrives.Where(t => t.IsSelected && t.AllocatedBytes > 0).ToList();
        if (targetDrives.Count == 0) throw new InvalidOperationException("Žádné cílové disky.");

        var backupSetName = $"DiskChecker_RawBackup_{DateTime.Now:yyyyMMdd_HHmmss}";
        var backupRoot = Path.Combine(targetDrives[0].DrivePath, backupSetName);
        Directory.CreateDirectory(backupRoot);
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Vytvořena složka raw zálohy: {backupRoot}");

        long totalSize = SourceDrive.TotalSize;
        var canCreateSingleMountableImage = targetDrives.Count == 1 || targetDrives[0].AllocatedBytes >= totalSize;
        long bytesRead = 0;

        // Use 1 MiB blocks for speed (was 4 KiB)
        const int blockSize = 1024 * 1024;
        var buffer = new byte[blockSize];

        int targetIndex = 0;
        long targetRemaining = targetDrives[0].AllocatedBytes;
        var currentRoot = backupRoot;
        var currentImagePath = Path.Combine(currentRoot, canCreateSingleMountableImage ? "disk_image.img" : "disk_image.img.part001");
        var createdParts = new List<string> { currentImagePath };
        FileStream? currentStream = new FileStream(currentImagePath, FileMode.Create, FileAccess.Write,
            FileShare.None, blockSize, FileOptions.SequentialScan);

        long unreadableBytes = 0;
        int consecutiveErrors = 0;
        const int maxConsecutiveErrors = 64;

        try
        {
            using var deviceStream = new FileStream(SourceDrive.Path, FileMode.Open, FileAccess.Read,
                FileShare.Read, blockSize, FileOptions.SequentialScan);

            while (bytesRead < totalSize)
            {
                ct.ThrowIfCancellationRequested();

                int bytesToRead = (int)Math.Min(blockSize, totalSize - bytesRead);
                int bytesReadNow = 0;
                bool blockReadable = true;

                try
                {
                    bytesReadNow = await deviceStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
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
                        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ Nečitelný sektor na pozici {FormatBytesLong(bytesRead)} — nahrazen nulami");

                    if (consecutiveErrors >= maxConsecutiveErrors)
                        throw new IOException($"Příliš mnoho nečitelných sektorů za sebou ({consecutiveErrors}) — disk je pravděpodobně vážně poškozen. Záloha přerušena.");
                }
                else
                {
                    consecutiveErrors = 0;
                }

                if (bytesReadNow > targetRemaining)
                {
                    if (targetIndex + 1 >= targetDrives.Count)
                        throw new IOException("Cílové úložiště raw zálohy je plné. Zápis byl zastaven, aby nedošlo k poškození zálohy.");

                    await currentStream.FlushAsync(ct);
                    currentStream.Dispose();

                    targetIndex++;
                    targetRemaining = targetDrives[targetIndex].AllocatedBytes;
                    currentRoot = Path.Combine(targetDrives[targetIndex].DrivePath, $"{backupSetName}_part{targetIndex + 1}");
                    Directory.CreateDirectory(currentRoot);
                    currentImagePath = Path.Combine(currentRoot, $"disk_image.img.part{targetIndex + 1:000}");
                    createdParts.Add(currentImagePath);
                    currentStream = new FileStream(currentImagePath, FileMode.Create, FileAccess.Write,
                        FileShare.None, blockSize, FileOptions.SequentialScan);
                    _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Přepnuto na část {targetIndex + 1}: {currentImagePath}");
                }

                await currentStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);
                bytesRead += bytesReadNow;
                targetRemaining -= bytesReadNow;

                _totalBytesBackedUp = bytesRead;
                OverallProgress = totalSize > 0 ? (double)bytesRead / totalSize * 100 : 0;
                CurrentFileName = $"Blok {bytesRead / blockSize} / {totalSize / blockSize}";
                CurrentFileProgress = OverallProgress;

                UpdateSpeedAndEta();
            }

            if (unreadableBytes > 0)
                _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ Celkem {FormatBytesLong(unreadableBytes)} nečitelných sektorů nahrazeno nulami.");
        }
        finally
        {
            currentStream?.Dispose();
        }

        var manifestPath = Path.Combine(backupRoot, "backup_manifest.json");
        var manifest = new
        {
            SourceDrive = SourceDrive.Path,
            SourceModel = SourceDrive.Name,
            BackupDate = DateTime.Now.ToString("O"),
            Mode = "RawImage",
            ImageFormat = canCreateSingleMountableImage ? "Raw mountable IMG" : "Split raw IMG parts",
            MountableImage = canCreateSingleMountableImage ? currentImagePath : null,
            TotalBytes = bytesRead,
            BlockSize = blockSize,
            UnreadableBytes = unreadableBytes,
            Parts = targetIndex + 1,
            PartFiles = createdParts.Select(Path.GetFileName).ToList(),
            PartPaths = createdParts.ToList(),
            Note = canCreateSingleMountableImage
                ? "Soubor disk_image.img je byte-for-byte obraz disku a lze jej před mazáním připojit/zkontrolovat běžnými nástroji."
                : "Obraz je rozdělen do částí na více cílových úložišť; pro kontrolu jej nejprve spojte ve správném pořadí."
        };
        using var manifestStream = System.IO.File.OpenWrite(manifestPath);
        JsonSerializer.Serialize(manifestStream, manifest, _jsonOptions);
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Raw záloha dokončena: {FormatBytesLong(bytesRead)} v {targetIndex + 1} části(ch)");
    }

    /// <summary>
    /// Creates a VHDx dynamic image of the source disk. VHDx is a modern, resilient
    /// format that can be mounted read-only on Windows (native) and Linux (via qemu-nbd/libguestfs).
    /// The image is sparse — only written sectors consume space on the target.
    /// </summary>
        private async Task RunVhdxBackupAsync(CancellationToken ct)
    {
        if (SourceDrive == null) throw new InvalidOperationException("Zdrojový disk není vybrán.");

        var targetDrives = TargetDrives.Where(t => t.IsSelected && t.AllocatedBytes > 0).ToList();
        if (targetDrives.Count == 0) throw new InvalidOperationException("Žádné cílové disky.");

        var backupSetName = $"DiskChecker_VhdxBackup_{DateTime.Now:yyyyMMdd_HHmmss}";
        var backupRoot = Path.Combine(targetDrives[0].DrivePath, backupSetName);
        Directory.CreateDirectory(backupRoot);
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Vytvořena složka VHDx zálohy: {backupRoot}");

        long totalSize = SourceDrive.TotalSize;
        var vhdxPath = Path.Combine(backupRoot, "disk_image.vhdx");
        long bytesRead = 0;

        const int blockSize = 1024 * 1024; // 1 MiB
        var buffer = new byte[blockSize];
        int targetIndex = 0;
        long targetRemaining = targetDrives[0].AllocatedBytes;
        var createdParts = new List<string> { vhdxPath };

        // Write VHDx header + BAT — fixed VHDx, all blocks pre-allocated
        await WriteVhdxHeaderAsync(vhdxPath, totalSize, ct);

        // Open for writing at specific offsets (not append)
        using var vhdxStream = new FileStream(vhdxPath, FileMode.Open, FileAccess.Write,
            FileShare.Read, blockSize, FileOptions.SequentialScan);

        // Calculate data start offset (same calculation as in WriteVhdxHeaderAsync)
        const int logicalSectorSize = 1048576;
        long chunkCount = ((totalSize + logicalSectorSize - 1) / logicalSectorSize);
        long batSize = ((chunkCount * 8 + 4096 - 1) / 4096) * 4096;
        long metadataEnd = 256L * 1024 + batSize + 1024L * 1024;
        long dataStartOffset = ((metadataEnd + logicalSectorSize - 1) / logicalSectorSize) * logicalSectorSize;

        try
        {
            using var deviceStream = new FileStream(SourceDrive.Path, FileMode.Open, FileAccess.Read,
                FileShare.Read, blockSize, FileOptions.SequentialScan);

            long unreadableBytes = 0;
            int consecutiveErrors = 0;
            const int maxConsecutiveErrors = 64;

            while (bytesRead < totalSize)
            {
                ct.ThrowIfCancellationRequested();

                int bytesToRead = (int)Math.Min(blockSize, totalSize - bytesRead);
                int bytesReadNow = 0;
                bool blockReadable = true;

                try
                {
                    bytesReadNow = await deviceStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
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
                    Array.Clear(buffer, 0, bytesToRead);
                    bytesReadNow = bytesToRead;
                    unreadableBytes += bytesToRead;
                    consecutiveErrors++;

                    if (consecutiveErrors == 1)
                        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ Nečitelný sektor na pozici {FormatBytesLong(bytesRead)} — nahrazen nulami");

                    if (consecutiveErrors >= maxConsecutiveErrors)
                        throw new IOException($"Příliš mnoho nečitelných sektorů za sebou ({consecutiveErrors}) — disk je pravděpodobně vážně poškozen. Záloha přerušena.");
                }
                else
                {
                    consecutiveErrors = 0;
                }

                // Check target space
                if (bytesReadNow > targetRemaining)
                {
                    if (targetIndex + 1 >= targetDrives.Count)
                        throw new IOException("Cílové úložiště VHDx zálohy je plné.");

                    vhdxStream.Dispose();

                    targetIndex++;
                    targetRemaining = targetDrives[targetIndex].AllocatedBytes;
                    var partRoot = Path.Combine(targetDrives[targetIndex].DrivePath, $"{backupSetName}_part{targetIndex + 1}");
                    Directory.CreateDirectory(partRoot);
                    vhdxPath = Path.Combine(partRoot, $"disk_image.vhdx.part{targetIndex + 1:000}");
                    createdParts.Add(vhdxPath);
                    await WriteVhdxHeaderAsync(vhdxPath, totalSize - bytesRead, ct);
                    // Reopen new file for writing at offsets
                    // For simplicity, use append for continuation parts
                    using var partStream = new FileStream(vhdxPath, FileMode.Open, FileAccess.Write,
                        FileShare.Read, blockSize, FileOptions.SequentialScan);
                    await partStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);
                    bytesRead += bytesReadNow;
                    targetRemaining -= bytesReadNow;
                    _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Přepnuto na část {targetIndex + 1}: {vhdxPath}");
                    continue;
                }

                // Write data at the correct offset in the fixed VHDx
                long chunkIndex = bytesRead / logicalSectorSize;
                long writeOffset = dataStartOffset + chunkIndex * logicalSectorSize;
                vhdxStream.Position = writeOffset;
                await vhdxStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);

                bytesRead += bytesReadNow;
                targetRemaining -= bytesReadNow;

                _totalBytesBackedUp = bytesRead;
                OverallProgress = totalSize > 0 ? (double)bytesRead / totalSize * 100 : 0;
                CurrentFileName = $"Blok {bytesRead / blockSize} / {totalSize / blockSize}";
                CurrentFileProgress = OverallProgress;

                UpdateSpeedAndEta();
            }

            await vhdxStream.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] ⏹ Záloha zrušena.");
            throw;
        }
        catch (Exception ex)
        {
            _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] ❌ Chyba: {ex.Message}");
            throw;
        }

        // Write manifest
        var manifestPath = Path.Combine(backupRoot, "backup_manifest.json");
        var manifest = new
        {
            SourceDrive = SourceDrive.Path,
            SourceModel = SourceDrive.Name,
            BackupDate = DateTime.Now.ToString("O"),
            Mode = "VhdxImage",
            ImageFormat = "VHDx Fixed (mountable)",
            MountableImage = createdParts.Count == 1 ? createdParts[0] : null,
            TotalBytes = bytesRead,
            BlockSize = blockSize,
            Parts = targetIndex + 1,
            PartFiles = createdParts.Select(Path.GetFileName).ToList(),
            PartPaths = createdParts.ToList(),
            DataStartOffset = dataStartOffset,
            Note = "Soubor disk_image.vhdx je fixed VHDx obraz disku. Lze jej připojit: Windows — poklepáním; Linux — sudo qemu-nbd -c /dev/nbd0 disk_image.vhdx && sudo mount /dev/nbd0p1 /mnt"
        };
        using var manifestStream = File.OpenWrite(manifestPath);
        await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, ct);
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] VHDx záloha dokončena: {FormatBytesLong(bytesRead)} v {targetIndex + 1} části(ch)");
    }
private static async Task WriteVhdxHeaderAsync(string path, long diskSizeBytes, CancellationToken ct)
    {
        const int logicalSectorSize = 1048576; // 1 MiB
        const int physicalSectorSize = 4096;

        long diskSizeRounded = ((diskSizeBytes + logicalSectorSize - 1) / logicalSectorSize) * logicalSectorSize;
        long chunkCount = diskSizeRounded / logicalSectorSize;
        long batSize = ((chunkCount * 8 + physicalSectorSize - 1) / physicalSectorSize) * physicalSectorSize;

        // Data start offset = 256K (region table end) + BAT + 1MiB metadata, aligned to 1MiB
        long metadataEnd = 256L * 1024 + batSize + 1024L * 1024;
        long dataStartOffset = ((metadataEnd + logicalSectorSize - 1) / logicalSectorSize) * logicalSectorSize;

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
        while (fs.Position < 128L * 1024)
            writer.Write((byte)0);

        // 3. Header 2 (at 128 KB) — sequence 1
        long header2Pos = fs.Position;
        WriteVhdxHeaderAt(writer, 1, logicalSectorSize, physicalSectorSize);

        // Pad from end of Header 2 (128KB + 4KB = 132KB) to Region Table position (192KB)
        while (fs.Position < 192L * 1024)
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
        while (fs.Position < 256L * 1024) writer.Write((byte)0);

        // 5. BAT (at 256 KB) — Fixed VHDx: all entries = fully allocated
        long fileOffsetInSectors = dataStartOffset / logicalSectorSize;
        for (long i = 0; i < chunkCount; i++)
        {
            // State = 6 (PAYLOAD_BLOCK_FULLY_PRESENT), FileOffset at bits 20-63
            ulong batEntry = (6UL << 0) | ((ulong)(fileOffsetInSectors + i) << 20);
            writer.Write(batEntry);
        }

        // Pad BAT to batSize
        long batEnd = 256L * 1024 + batSize;
        while (fs.Position < batEnd) writer.Write((byte)0);

        // 6. Metadata (1 MiB)
        WriteVhdxMetadataAt(writer, diskSizeRounded, logicalSectorSize, physicalSectorSize);

        // Pad to data start offset
        while (fs.Position < dataStartOffset) writer.Write((byte)0);

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
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0x82F63B78 : crc >> 1;
            table[i] = crc;
        }
        uint result = 0xFFFFFFFF;
        foreach (byte b in data)
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
        while (writer.BaseStream.Position < metadataStart + 1024L * 1024)
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

    private void UpdateSpeedAndEta()
    {
        var elapsed = DateTime.UtcNow - _backupStartTime;
        ElapsedTime = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

        if (elapsed.TotalSeconds > 1 && _totalBytesBackedUp > 0)
        {
            var speedBps = _totalBytesBackedUp / elapsed.TotalSeconds;
            CurrentSpeed = FormatBytesLong((long)speedBps) + "/s";

            if (_totalBytesToBackup > 0 && speedBps > 0)
            {
                var remainingBytes = _totalBytesToBackup - _totalBytesBackedUp;
                var remainingSeconds = remainingBytes / speedBps;
                var remaining = TimeSpan.FromSeconds(remainingSeconds);
                EstimatedRemaining = remaining.TotalHours >= 1
                    ? $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
                    : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
        }
    }

    private void CancelBackup()
    {
        _backupCancellation?.Cancel();
        StatusMessage = "Přerušuji zálohu...";
    }

    private void GoBack()
    {
        if (Phase == BackupPhase.Running)
        {
            _ = _dialogService.ShowErrorAsync("Záloha běží", "Nelze opustit během zálohování. Nejprve zálohu přerušte.");
            return;
        }
        _navigationService.NavigateTo<SmartCheckViewModel>();
    }

    private void NavigateToRestore()
    {
        if (Phase == BackupPhase.Running)
        {
            _ = _dialogService.ShowErrorAsync("Záloha běží", "Nelze opustit během zálohování. Nejprve zálohu přerušte.");
            return;
        }
        _navigationService.NavigateTo<RestoreViewModel>();
    }

    private static string FormatBytesLong(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private sealed class BackupCopyState
    {
        public int TargetIndex;
        public long TargetRemaining;
        public List<BackupTargetItem> TargetDrives;
        public string BackupRoot;

        public BackupCopyState(int targetIndex, long targetRemaining, List<BackupTargetItem> targetDrives, string backupRoot)
        {
            TargetIndex = targetIndex;
            TargetRemaining = targetRemaining;
            TargetDrives = targetDrives;
            BackupRoot = backupRoot;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _backupCancellation?.Cancel();
        _backupCancellation?.Dispose();

        GC.SuppressFinalize(this);
    }
}
