using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    
    [ObservableProperty] private bool _isSelected;
}

public enum BackupMode
{
    FileLevel,
    RawImage
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
    public IRelayCommand SwitchToRawModeCommand { get; }
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
        SwitchToRawModeCommand = new RelayCommand(() => SelectedMode = BackupMode.RawImage);
        SwitchToFileModeCommand = new RelayCommand(() => SelectedMode = BackupMode.FileLevel);
        NavigateToRestoreCommand = new RelayCommand(NavigateToRestore);
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

    private async Task LoadTargetDrivesAsync()
    {
        TargetDrives.Clear();

        var allDrives = System.IO.DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType != DriveType.CDRom)
            .ToList();

        await CalculateSystemReserveAsync();

        foreach (var drive in allDrives)
        {
            if (SourceDrive != null &&
                (drive.Name.StartsWith(SourceDrive.Path, StringComparison.OrdinalIgnoreCase) ||
                 SourceDrive.Path.StartsWith(drive.Name, StringComparison.OrdinalIgnoreCase)))
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
            var volumes = System.IO.DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .ToList();

            var matchingVolume = volumes.FirstOrDefault(v =>
                v.Name.StartsWith(SourceDrive.Path, StringComparison.OrdinalIgnoreCase) ||
                SourceDrive.Path.StartsWith(v.Name, StringComparison.OrdinalIgnoreCase));

            if (matchingVolume == null) return;

            var rootPath = matchingVolume.RootDirectory.FullName;
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

    private void CalculateSpaceRequirements()
    {
        if (SelectedMode == BackupMode.RawImage)
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
            $"Režim: {(SelectedMode == BackupMode.RawImage ? "Raw obraz" : "File-level")}\n" +
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

        foreach (var folder in selectedFolders)
        {
            ct.ThrowIfCancellationRequested();

            var folderName = new DirectoryInfo(folder.Path).Name;
            var destFolder = Path.Combine(copyState.BackupRoot, folderName);
            Directory.CreateDirectory(destFolder);

            _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Zálohuji: {folder.Path} → {destFolder}");

            await CopyDirectoryAsync(folder.Path, destFolder, copyState, ct);
        }

        var manifestPath = Path.Combine(backupRoot, "backup_manifest.json");
        var manifest = new
        {
            SourceDrive = SourceDrive?.Path,
            SourceModel = SourceDrive?.Name,
            BackupDate = DateTime.Now.ToString("O"),
            Mode = "FileLevel",
            TotalBytes = _totalBytesBackedUp,
            Folders = selectedFolders.Select(f => f.Path).ToList()
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

        var backupRoot = Path.Combine(targetDrives[0].DrivePath, $"DiskChecker_RawBackup_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(backupRoot);
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Vytvořena složka raw zálohy: {backupRoot}");

        long totalSize = SourceDrive.TotalSize;
        long bytesRead = 0;
        const int sectorSize = 4096;
        var buffer = new byte[sectorSize];

        int targetIndex = 0;
        long targetRemaining = targetDrives[0].AllocatedBytes;
        var currentImagePath = Path.Combine(backupRoot, "disk_image_part1.raw");
        FileStream? currentStream = new FileStream(currentImagePath, FileMode.Create, FileAccess.Write,
            FileShare.None, 1024 * 1024, FileOptions.SequentialScan);

        try
        {
            using var deviceStream = new FileStream(SourceDrive.Path, FileMode.Open, FileAccess.Read,
                FileShare.Read, sectorSize, FileOptions.SequentialScan);

            while (bytesRead < totalSize)
            {
                ct.ThrowIfCancellationRequested();

                int bytesToRead = (int)Math.Min(sectorSize, totalSize - bytesRead);
                int bytesReadNow = await deviceStream.ReadAsync(buffer.AsMemory(0, bytesToRead), ct);
                if (bytesReadNow == 0) break;

                if (bytesReadNow > targetRemaining && targetIndex + 1 < targetDrives.Count)
                {
                    await currentStream.FlushAsync(ct);
                    currentStream.Dispose();

                    targetIndex++;
                    targetRemaining = targetDrives[targetIndex].AllocatedBytes;
                    currentImagePath = Path.Combine(backupRoot, $"disk_image_part{targetIndex + 1}.raw");
                    currentStream = new FileStream(currentImagePath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
                    _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Přepnuto na část {targetIndex + 1}: {currentImagePath}");
                }

                await currentStream.WriteAsync(buffer.AsMemory(0, bytesReadNow), ct);
                bytesRead += bytesReadNow;
                targetRemaining -= bytesReadNow;

                _totalBytesBackedUp = bytesRead;
                OverallProgress = totalSize > 0 ? (double)bytesRead / totalSize * 100 : 0;
                CurrentFileName = $"Sektor {bytesRead / sectorSize} / {totalSize / sectorSize}";
                CurrentFileProgress = OverallProgress;

                UpdateSpeedAndEta();
            }
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
            TotalBytes = bytesRead,
            SectorSize = sectorSize,
            Parts = targetIndex + 1
        };
        using var manifestStream = System.IO.File.OpenWrite(manifestPath);
        JsonSerializer.Serialize(manifestStream, manifest, _jsonOptions);
        _backupLog.Add($"[{DateTime.Now:HH:mm:ss}] Raw záloha dokončena: {FormatBytesLong(bytesRead)} v {targetIndex + 1} části(ch)");
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
