using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;
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
    private string? _lastBackupManifestPath;
    private string? _imageMountVerificationMessage;
    private bool _imageMountWasVerified;
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
    [ObservableProperty] private bool _canConvertRawToVhdx;

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
    public IAsyncRelayCommand ConvertRawToVhdxCommand { get; }

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
        ConvertRawToVhdxCommand = new AsyncRelayCommand(ConvertLastRawBackupToVhdxAsync, () => CanConvertRawToVhdx && Phase != BackupPhase.Running);

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
        ConvertRawToVhdxCommand?.NotifyCanExecuteChanged();
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

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            await errorTask;
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task LoadTargetDrivesAsync()
    {
        TargetDrives.Clear();

        IEnumerable<System.IO.DriveInfo> allDrives;
        if (OperatingSystem.IsLinux())
        {
            var json = await ExecuteCommandAsync("lsblk", "--json --paths --output NAME,KNAME,PATH,TYPE,FSTYPE,RO,MOUNTPOINTS,MAJ:MIN");
            if (string.IsNullOrWhiteSpace(json))
                throw new IOException("Nelze načíst disková úložiště pomocí lsblk.");
            allDrives = LinuxBackupTargetDiscovery.GetMountPoints(json, SourceDrive?.Path)
                .Select(path => new System.IO.DriveInfo(path));
        }
        else
        {
            allDrives = System.IO.DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType != DriveType.CDRom && d.DriveType != DriveType.Ram);
        }

        await CalculateSystemReserveAsync();

        foreach (var drive in allDrives)
        {
            // Linux source ancestry has already been excluded from the lsblk tree.
            if (!OperatingSystem.IsLinux() && SourceDrive != null &&
                (drive.Name.StartsWith(SourceDrive.Path, StringComparison.OrdinalIgnoreCase) ||
                 SourceDrive.Path.StartsWith(drive.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                if (!drive.IsReady || drive.AvailableFreeSpace <= 0) continue;
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
            catch (IOException) { /* A removable target may disappear during discovery. */ }
            catch (UnauthorizedAccessException) { /* Inaccessible filesystems cannot be targets. */ }
        }

        await Task.CompletedTask;
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
        if (SelectedMode == BackupMode.VhdxImage)
        {
            TotalRequiredBytes = SourceDrive?.TotalSize > 0 ? VhdxImageUtility.CalculateRequiredFileBytes(SourceDrive.TotalSize) : 0;
        }
        else if (SelectedMode == BackupMode.RawImage)
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

        if (SelectedMode == BackupMode.VhdxImage)
        {
            // VHDx is one mountable file and cannot be safely split across targets.
            // The old check used only the source disk size and allocated only that amount,
            // then the writer correctly required VHDx metadata/BAT overhead and failed by a few MB.
            HasEnoughSpace = TargetDrives.Any(t => GetUsableTargetBytes(t) >= TotalRequiredBytes);
        }
        else
        {
            HasEnoughSpace = usableBytes >= TotalRequiredBytes;
        }

        if (HasEnoughSpace)
        {
            SpaceSummary = SelectedMode == BackupMode.VhdxImage
                ? $"✓ Dostatek místa pro VHDx: potřeba {TotalRequiredText} v jednom souboru, k dispozici {TotalAvailableText} (rezerva {SystemReserveText})"
                : $"✓ Dostatek místa: potřeba {TotalRequiredText}, k dispozici {TotalAvailableText} (rezerva {SystemReserveText})";
        }
        else
        {
            var availableForMode = SelectedMode == BackupMode.VhdxImage
                ? TargetDrives.Select(GetUsableTargetBytes).DefaultIfEmpty(0).Max()
                : usableBytes;
            var deficit = Math.Max(0, TotalRequiredBytes - availableForMode);
            SpaceSummary = SelectedMode == BackupMode.VhdxImage
                ? $"⚠ Nedostatek místa pro VHDx: potřeba {TotalRequiredText} na jednom cíli, chybí {FormatBytesLong(deficit)}"
                : $"⚠ Nedostatek místa: potřeba {TotalRequiredText}, k dispozici {TotalAvailableText}, chybí {FormatBytesLong(deficit)}";
        }

        DistributeAllocation();
    }

    private long GetUsableTargetBytes(BackupTargetItem target)
    {
        var selectedCount = Math.Max(1, TargetDrives.Count(t => t.IsSelected));
        return Math.Max(0, target.TotalFreeSpace - (SystemReserveBytes / selectedCount));
    }

    private void DistributeAllocation()
    {
        foreach (var target in TargetDrives)
            target.AllocatedBytes = 0;

        if (SelectedMode == BackupMode.VhdxImage)
        {
            var best = TargetDrives
                .Where(t => GetUsableTargetBytes(t) >= TotalRequiredBytes)
                .OrderByDescending(t => GetUsableTargetBytes(t))
                .FirstOrDefault()
                ?? TargetDrives.OrderByDescending(GetUsableTargetBytes).FirstOrDefault();

            if (best != null)
            {
                foreach (var target in TargetDrives)
                    target.IsSelected = target == best;
                best.AllocatedBytes = Math.Min(TotalRequiredBytes, GetUsableTargetBytes(best));
            }
            return;
        }

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
            L.Get("Backup.ConfirmTitle"),
            string.Format(L.Get("Backup.ConfirmMessage"),
                SourceDrive?.Name ?? "?", SourceDrive?.Path ?? "?",
                SelectedMode == BackupMode.RawImage ? L.Get("Backup.Mode.Raw") : SelectedMode == BackupMode.VhdxImage ? L.Get("Backup.Mode.Vhdx") : L.Get("Backup.Mode.FileLevel"),
                TotalRequiredText));


        if (!confirmed) return;

        _backupCancellation = new CancellationTokenSource();
        _backupStartTime = DateTime.UtcNow;
        _totalBytesToBackup = TotalRequiredBytes;
        _totalBytesBackedUp = 0;
        _backupLog.Clear();
        _imageMountVerificationMessage = null;
        _imageMountWasVerified = false;
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
            StatusMessage = _imageMountVerificationMessage == null
                ? "✅ Záloha dokončena!"
                : (_imageMountWasVerified ? "✅ Záloha dokončena. " : "⚠ Záloha vytvořena, ale připojení nebylo ověřeno. ") +
                  _imageMountVerificationMessage;
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
        _lastBackupManifestPath = manifestPath;
        CanConvertRawToVhdx = true;
        ConvertRawToVhdxCommand.NotifyCanExecuteChanged();
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Raw záloha dokončena: {FormatBytesLong(bytesRead)} v {targetIndex + 1} části(ch)");
        // Linux can expose a single RAW .img as a read-only NBD block device.
        // Split images cannot be attached until all parts have been joined.
        if (OperatingSystem.IsLinux() && canCreateSingleMountableImage &&
            createdParts.Count == 1 && bytesRead == totalSize)
            await VerifyAndOfferMountedImageAsync(currentImagePath, ct);
    }

    /// <summary>
    /// Creates a fixed VHDX image of the source disk. VHDX is a modern, resilient
    /// format that can be mounted read-only on Windows (native) and Linux (via qemu-nbd/libguestfs).
    /// All payload blocks are present and the virtual size matches the source exactly.
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

        var dataStartOffset = VhdxImageUtility.CalculateDataStartOffset(totalSize);
        var requiredBytes = VhdxImageUtility.CalculateRequiredFileBytes(totalSize);
        var targetUsableBytes = GetUsableTargetBytes(targetDrives[0]);
        if (targetUsableBytes < requiredBytes)
            throw new IOException($"Cílový disk nemá dost místa pro jeden platný VHDx soubor. Potřeba {FormatBytesLong(requiredBytes)}, dostupné {FormatBytesLong(targetUsableBytes)}. VHDx zálohu nelze bezpečně dělit na více částí; vyberte větší cílový disk nebo použijte raw režim.");
        targetDrives[0].AllocatedBytes = requiredBytes;

        // Write VHDx header + BAT — fixed VHDx, all blocks pre-allocated
        await VhdxImageUtility.WriteFixedHeaderAsync(vhdxPath, totalSize, ct);

        // Open for writing at specific offsets (not append)
        using var vhdxStream = new FileStream(vhdxPath, FileMode.Open, FileAccess.Write,
            FileShare.Read, blockSize, FileOptions.SequentialScan);

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
                    deviceStream.Position = bytesRead;
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

                // Write data sequentially into the VHDx payload area.
                // Header creation already pre-extends the file to the rounded payload size.
                vhdxStream.Position = dataStartOffset + bytesRead;
                await vhdxStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);

                bytesRead += bytesReadNow;

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

        vhdxStream.Dispose();
        VhdxImageUtility.ValidateFixedImageLength(vhdxPath, totalSize);

        // Write manifest
        var manifestPath = Path.Combine(backupRoot, "backup_manifest.json");
        var manifest = new
        {
            SourceDrive = SourceDrive.Path,
            SourceModel = SourceDrive.Name,
            BackupDate = DateTime.Now.ToString("O"),
            Mode = "VhdxImage",
            ImageFormat = "VHDx Fixed (mountable)",
            MountableImage = vhdxPath,
            TotalBytes = bytesRead,
            BlockSize = blockSize,
            ImageFileBytes = new FileInfo(vhdxPath).Length,
            VirtualDiskBytes = totalSize,
            Parts = 1,
            PartFiles = new[] { Path.GetFileName(vhdxPath) },
            PartPaths = new[] { vhdxPath },
            DataStartOffset = dataStartOffset,
            Note = "Soubor disk_image.vhdx je fixed VHDx obraz disku. Lze jej připojit: Windows — poklepáním; Linux — sudo qemu-nbd -c /dev/nbd0 disk_image.vhdx && sudo mount /dev/nbd0p1 /mnt"
        };
        using var manifestStream = File.OpenWrite(manifestPath);
        await JsonSerializer.SerializeAsync(manifestStream, manifest, _jsonOptions, ct);
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] VHDx záloha dokončena: {FormatBytesLong(bytesRead)} v souboru s ověřenou strukturou");
        await VerifyAndOfferMountedImageAsync(vhdxPath, ct);
    }

    /// <summary>
    /// Mount validation is an independent step after image creation. A failed
    /// mount never deletes the backup, nor is it misreported as verified.
    /// The operator may leave a successful mount accessible read-only.
    /// </summary>
    private async Task VerifyAndOfferMountedImageAsync(string imagePath, CancellationToken ct)
    {
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Zkouším systémově připojit obraz jen pro čtení: {imagePath}");
        CurrentFileName = "Ověřuji připojitelnost obrazu...";
        var attempt = await ReadOnlyImageMountService.TryAttachAsync(imagePath, ct);
        _imageMountWasVerified = attempt.Verified;
        _imageMountVerificationMessage = attempt.Message;
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] {(attempt.Verified ? "✓" : "⚠")} {attempt.Message}");

        if (attempt.Mount == null)
        {
            await _dialogService.ShowWarningAsync("Kontrola obrazu",
                "Záloha byla uložena, ale systémové připojení se nepodařilo ověřit. " +
                "Soubor není smazán.\\n\\n" + attempt.Message);
            return;
        }

        if (!attempt.Mount.OwnedByDiskChecker)
        {
            _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Obraz byl připojen už před kontrolou; DiskChecker ho nebude odpojovat.");
            return;
        }

        var leaveMounted = false;
        try
        {
            leaveMounted = await _dialogService.ShowConfirmationAsync(
                "Obraz je připojen jen pro čtení",
                attempt.Message + "\\n\\n" +
                "ANO = ponechat připojený obraz pro procházení souborů.\\n" +
                "NE = nyní ho bezpečně odpojit.\\n\\n" +
                "Pozor: připojení obrazu neprokazuje, že každý soubor v záloze je nepoškozený.");
        }
        finally
        {
            if (!leaveMounted)
            {
                try
                {
                    await ReadOnlyImageMountService.DetachAsync(attempt.Mount, CancellationToken.None);
                    _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Ověřený obraz byl bezpečně odpojen.");
                }
                catch (Exception ex)
                {
                    _imageMountVerificationMessage = $"Obraz ověřen, ale automatické odpojení selhalo: {ex.Message}. " +
                        $"Připojení: {attempt.Mount.AccessPath}";
                    _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] ⚠ {_imageMountVerificationMessage}");
                }
            }
            else
            {
                _imageMountVerificationMessage = $"Obraz ponechán připojený jen pro čtení: {attempt.Mount.AccessPath}";
                _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Obraz ponechán připojený: {attempt.Mount.AccessPath}");
            }
        }
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
            _ = _dialogService.ShowErrorAsync(L.Get("Common.BackupRunning"), L.Get("Common.CannotLeaveDuringBackup"));
            return;
        }
        _navigationService.NavigateTo<SmartCheckViewModel>();
    }


    private async Task ConvertLastRawBackupToVhdxAsync()
    {
        if (string.IsNullOrWhiteSpace(_lastBackupManifestPath) || !File.Exists(_lastBackupManifestPath))
        {
            await _dialogService.ShowErrorAsync(L.Get("Common.RawBackupNotFound"), L.Get("Common.RawBackupNotFoundMsg"));
            return;
        }

        try
        {
            StatusMessage = "Převádím RAW zálohu na VHDx...";
            _imageMountWasVerified = false;
            _imageMountVerificationMessage = null;
            await ConvertRawManifestToVhdxAsync(_lastBackupManifestPath, _backupCancellation?.Token ?? CancellationToken.None);
            BackupLogText = string.Join("\n", _backupLog);
            StatusMessage = _imageMountVerificationMessage == null ? "RAW záloha byla převedena na VHDx."
                : (_imageMountWasVerified ? "✅ VHDx vytvořen a připojení ověřeno. "
                   : "⚠ VHDx vytvořen, systémové připojení se nepodařilo ověřit. ") +
                  _imageMountVerificationMessage;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(L.Get("Common.RawToVhdxFailed"), ex.Message);
            _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Převod RAW → VHDx selhal: {ex.Message}");
            BackupLogText = string.Join("\n", _backupLog);
        }
        finally
        {
            ConvertRawToVhdxCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ConvertRawManifestToVhdxAsync(string manifestPath, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
        var root = doc.RootElement;
        var mode = root.TryGetProperty("Mode", out var modeEl) ? modeEl.GetString() : null;
        if (!string.Equals(mode, "RawImage", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Vybraná záloha není RAW image.");

        var totalBytes = root.TryGetProperty("TotalBytes", out var tbEl) ? tbEl.GetInt64() : 0;
        if (totalBytes <= 0) throw new InvalidOperationException("Manifest RAW zálohy neobsahuje platnou velikost.");

        var rawParts = new List<string>();
        if (root.TryGetProperty("PartPaths", out var partsEl) && partsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in partsEl.EnumerateArray())
            {
                var path = part.GetString();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) rawParts.Add(path);
            }
        }
        if (rawParts.Count == 0 && root.TryGetProperty("MountableImage", out var mountEl))
        {
            var path = mountEl.GetString();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) rawParts.Add(path);
        }
        if (rawParts.Count == 0)
        {
            var rootDir = Path.GetDirectoryName(manifestPath)!;
            rawParts.AddRange(Directory.GetFiles(rootDir, "disk_image.img*", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
            rawParts.AddRange(Directory.GetFiles(rootDir, "disk_image.raw", SearchOption.TopDirectoryOnly));
        }
        if (rawParts.Count == 0) throw new FileNotFoundException("RAW image/části nebyly nalezeny.");

        var backupRoot = Path.GetDirectoryName(manifestPath)!;
        var vhdxPath = Path.Combine(backupRoot, "disk_image_converted.vhdx");
        var dataStartOffset = VhdxImageUtility.CalculateDataStartOffset(totalBytes);
        await VhdxImageUtility.WriteFixedHeaderAsync(vhdxPath, totalBytes, ct);

        const int blockSize = 1024 * 1024;
        var buffer = new byte[blockSize];
        long copied = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var vhdxStream = new FileStream(vhdxPath, FileMode.Open, FileAccess.Write, FileShare.Read, blockSize, FileOptions.SequentialScan);

        foreach (var partPath in rawParts)
        {
            await using var rawStream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, blockSize, FileOptions.SequentialScan);
            while (copied < totalBytes)
            {
                ct.ThrowIfCancellationRequested();
                var toRead = (int)Math.Min(buffer.Length, totalBytes - copied);
                var read = await rawStream.ReadAsync(buffer.AsMemory(0, toRead), ct);
                if (read == 0) break;
                vhdxStream.Position = dataStartOffset + copied;
                await vhdxStream.WriteAsync(buffer.AsMemory(0, read), ct);
                hash.AppendData(buffer, 0, read);
                copied += read;
                _totalBytesBackedUp = copied;
                OverallProgress = totalBytes > 0 ? (double)copied / totalBytes * 100 : 0;
                CurrentFileName = $"RAW → VHDx {FormatBytesLong(copied)} / {FormatBytesLong(totalBytes)}";
                UpdateSpeedAndEta();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, ct);
            }
        }
        await vhdxStream.FlushAsync(ct);
        await vhdxStream.DisposeAsync(); // Release the writer before an OS attempts to attach the image.
        if (copied != totalBytes) throw new IOException($"Převedeno jen {FormatBytesLong(copied)} z {FormatBytesLong(totalBytes)}.");

        var sha = Convert.ToHexString(hash.GetHashAndReset());
        VhdxImageUtility.ValidateFixedImageLength(vhdxPath, totalBytes);
        File.SetAttributes(vhdxPath, File.GetAttributes(vhdxPath) | FileAttributes.ReadOnly);
        var convertedManifest = new
        {
            SourceManifest = manifestPath,
            BackupDate = DateTime.Now.ToString("O"),
            Mode = "VhdxImage",
            ImageFormat = "VHDx Fixed converted from RAW",
            MountableImage = vhdxPath,
            TotalBytes = totalBytes,
            DataStartOffset = dataStartOffset,
            ImageFileBytes = new FileInfo(vhdxPath).Length,
            VirtualDiskBytes = totalBytes,
            Sha256 = sha,
            ReadOnly = true,
            Note = "VHDx vytvořený z RAW zálohy. Soubor byl označen jako read-only; připojujte ideálně pouze pro čtení."
        };
        var convertedManifestPath = Path.Combine(backupRoot, "backup_manifest_converted_vhdx.json");
        await using var manifestStream = File.Open(convertedManifestPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(manifestStream, convertedManifest, _jsonOptions, ct);
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] RAW → VHDx hotovo: {vhdxPath} ({FormatBytesLong(totalBytes)})");
        await VerifyAndOfferMountedImageAsync(vhdxPath, ct);
    }

    private void NavigateToRestore()
    {
        if (Phase == BackupPhase.Running)
        {
            _ = _dialogService.ShowErrorAsync(L.Get("Common.BackupRunning"), L.Get("Common.CannotLeaveDuringBackup"));
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
