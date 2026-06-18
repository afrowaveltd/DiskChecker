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
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Represents a discovered backup that can be restored.
/// </summary>
public partial class DiscoveredBackup : ObservableObject
{
    public string ManifestPath { get; init; } = string.Empty;
    public string BackupRoot { get; init; } = string.Empty;
    public string SourceDrivePath { get; init; } = string.Empty;
    public string SourceModel { get; init; } = string.Empty;
    public string BackupDate { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public string TotalBytesText { get; init; } = string.Empty;
    public int Parts { get; init; } = 1;
    public List<string> Folders { get; init; } = new();
    public string FoldersSummary { get; init; } = string.Empty;
}

/// <summary>
/// Represents a target disk for restore.
/// </summary>
public partial class RestoreTargetItem : ObservableObject
{
    public string DrivePath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public long TotalSize { get; init; }
    public string TotalSizeText { get; init; } = string.Empty;
    public bool IsSourceDisk { get; init; }
    public string Warning { get; init; } = string.Empty;

    [ObservableProperty] private bool _isSelected;
}

public enum RestorePhase
{
    Idle,
    Scanning,
    Ready,
    Running,
    Verifying,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Orchestrates restore of a backup to a new disk.
/// Supports file-level restore and raw image restore.
/// </summary>
public partial class RestoreViewModel : ViewModelBase, INavigableViewModel, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly ISelectedDiskService _selectedDiskService;
    private readonly IDialogService _dialogService;

    private CancellationTokenSource? _restoreCancellation;
    private bool _disposed;
    private DateTime _restoreStartTime;
    private long _totalBytesToRestore;
    private long _totalBytesRestored;
    private readonly List<string> _restoreLog = new();

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    [ObservableProperty] private RestorePhase _phase = RestorePhase.Idle;
    [ObservableProperty] private string _statusMessage = "Připraven k obnově";
    [ObservableProperty] private DiscoveredBackup? _selectedBackup;
    [ObservableProperty] private bool _hasSelectedBackup;

    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private double _currentFileProgress;
    [ObservableProperty] private long _currentFileBytes;
    [ObservableProperty] private string _currentFileName = string.Empty;
    [ObservableProperty] private string _currentSpeed = "—";
    [ObservableProperty] private string _elapsedTime = "00:00";
    [ObservableProperty] private string _estimatedRemaining = "--:--";

    [ObservableProperty] private string _restoreLogText = string.Empty;
    [ObservableProperty] private string _verifyResult = string.Empty;
    [ObservableProperty] private bool _isVerifyOk;

    public ObservableCollection<DiscoveredBackup> DiscoveredBackups { get; } = new();
    public ObservableCollection<RestoreTargetItem> TargetDisks { get; } = new();

    public IAsyncRelayCommand ScanForBackupsCommand { get; }
    public IAsyncRelayCommand StartRestoreCommand { get; }
    public IRelayCommand CancelRestoreCommand { get; }
    public IRelayCommand GoBackCommand { get; }
    public IRelayCommand SelectBackupCommand { get; }

    public RestoreViewModel(
        INavigationService navigationService,
        ISelectedDiskService selectedDiskService,
        IDialogService dialogService)
    {
        _navigationService = navigationService;
        _selectedDiskService = selectedDiskService;
        _dialogService = dialogService;

        ScanForBackupsCommand = new AsyncRelayCommand(ScanForBackupsAsync);
        StartRestoreCommand = new AsyncRelayCommand(StartRestoreAsync,
            () => Phase == RestorePhase.Ready && HasSelectedBackup && TargetDisks.Any(t => t.IsSelected));
        CancelRestoreCommand = new RelayCommand(CancelRestore);
        GoBackCommand = new RelayCommand(GoBack);
        SelectBackupCommand = new RelayCommand(SelectBackup);
    }

    public void OnNavigatedTo()
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        Phase = RestorePhase.Scanning;
        StatusMessage = "Hledám existující zálohy...";
        await ScanForBackupsAsync();
        await ScanTargetDisksAsync();

        if (DiscoveredBackups.Count > 0)
        {
            Phase = RestorePhase.Ready;
            StatusMessage = $"Nalezeno {DiscoveredBackups.Count} záloh. Vyberte zálohu a cílový disk.";
        }
        else
        {
            Phase = RestorePhase.Idle;
            StatusMessage = "Nenalezeny žádné zálohy. Nejprve proveďte zálohu disku.";
        }
    }

    private async Task ScanForBackupsAsync()
    {
        DiscoveredBackups.Clear();

        try
        {
            var allDrives = System.IO.DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType != DriveType.CDRom)
                .ToList();

            foreach (var drive in allDrives)
            {
                await Task.Run(() => ScanDriveForBackups(drive.RootDirectory.FullName));
            }

            // Also scan common backup locations
            var commonPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DiskChecker_Backup"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DiskChecker_Backup"),
                Path.Combine(Path.GetTempPath(), "DiskChecker_Backup")
            };

            foreach (var path in commonPaths)
            {
                if (Directory.Exists(path))
                    await Task.Run(() => ScanDirectoryForBackups(path));
            }
        }
        catch (Exception ex)
        {
            _restoreLog.Add($"[SCAN ERROR] {ex.Message}");
        }

        // Sort by date, newest first
        var sorted = DiscoveredBackups
            .OrderByDescending(b => b.BackupDate)
            .ToList();

        DiscoveredBackups.Clear();
        foreach (var b in sorted)
            DiscoveredBackups.Add(b);
    }

    private void ScanDriveForBackups(string rootPath)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(rootPath, "DiskChecker_Backup_*", SearchOption.TopDirectoryOnly))
            {
                ScanDirectoryForBackups(dir);
            }
            foreach (var dir in Directory.GetDirectories(rootPath, "DiskChecker_RawBackup_*", SearchOption.TopDirectoryOnly))
            {
                ScanDirectoryForBackups(dir);
            }
        }
        catch { }
    }

    private void ScanDirectoryForBackups(string directoryPath)
    {
        try
        {
            var manifestPath = Path.Combine(directoryPath, "backup_manifest.json");
            if (!File.Exists(manifestPath)) return;

            var json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var mode = root.TryGetProperty("Mode", out var modeEl) ? modeEl.GetString() ?? "Unknown" : "Unknown";
            var totalBytes = root.TryGetProperty("TotalBytes", out var tbEl) ? tbEl.GetInt64() : 0;
            var parts = root.TryGetProperty("Parts", out var pEl) ? pEl.GetInt32() : 1;
            var sourceDrive = root.TryGetProperty("SourceDrive", out var sdEl) ? sdEl.GetString() ?? "?" : "?";
            var sourceModel = root.TryGetProperty("SourceModel", out var smEl) ? smEl.GetString() ?? "?" : "?";
            var backupDate = root.TryGetProperty("BackupDate", out var bdEl) ? bdEl.GetString() ?? "?" : "?";

            var folders = new List<string>();
            if (root.TryGetProperty("Folders", out var fEl) && fEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fEl.EnumerateArray())
                    folders.Add(f.GetString() ?? "?");
            }

            var foldersSummary = folders.Count switch
            {
                0 => mode == "RawImage" ? "Raw obraz disku" : "—",
                1 => new DirectoryInfo(folders[0]).Name,
                _ => $"{new DirectoryInfo(folders[0]).Name} + {folders.Count - 1} dalších"
            };

            // Parse date for display
            var dateDisplay = backupDate;
            if (DateTime.TryParse(backupDate, out var dt))
                dateDisplay = dt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

            Dispatcher.UIThread.Post(() =>
            {
                DiscoveredBackups.Add(new DiscoveredBackup
                {
                    ManifestPath = manifestPath,
                    BackupRoot = directoryPath,
                    SourceDrivePath = sourceDrive,
                    SourceModel = sourceModel,
                    BackupDate = dateDisplay,
                    Mode = mode == "RawImage" ? "Raw obraz" : "Souborová",
                    TotalBytes = totalBytes,
                    TotalBytesText = FormatBytesLong(totalBytes),
                    Parts = parts,
                    Folders = folders,
                    FoldersSummary = foldersSummary
                });
            });
        }
        catch { }
    }

    private async Task ScanTargetDisksAsync()
    {
        TargetDisks.Clear();

        var allDrives = System.IO.DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .ToList();

        foreach (var drive in allDrives)
        {
            var isSource = SelectedBackup != null &&
                (drive.Name.StartsWith(SelectedBackup.SourceDrivePath, StringComparison.OrdinalIgnoreCase) ||
                 SelectedBackup.SourceDrivePath.StartsWith(drive.Name, StringComparison.OrdinalIgnoreCase));

            string warning = "";
            if (isSource)
                warning = "⚠️ Toto je původní disk! Obnova na něj může přepsat data.";
            else if (drive.DriveType == DriveType.CDRom)
                warning = "CD/DVD – nelze použít";

            TargetDisks.Add(new RestoreTargetItem
            {
                DrivePath = drive.Name,
                DisplayName = $"{drive.Name} ({drive.VolumeLabel}) — {drive.DriveFormat}",
                TotalSize = drive.TotalSize,
                TotalSizeText = FormatBytesLong(drive.TotalSize),
                IsSourceDisk = isSource,
                Warning = warning,
                IsSelected = false
            });
        }

        // Also add physical drives for raw restore
        try
        {
            var physicalDrives = System.IO.DriveInfo.GetDrives()
                .Where(d => !d.IsReady)
                .Select(d => d.Name)
                .ToList();

            foreach (var physPath in physicalDrives)
            {
                if (TargetDisks.Any(t => t.DrivePath == physPath)) continue;

                TargetDisks.Add(new RestoreTargetItem
                {
                    DrivePath = physPath,
                    DisplayName = $"{physPath} (nepřipojený/logický disk)",
                    TotalSize = 0,
                    TotalSizeText = "?",
                    IsSourceDisk = false,
                    Warning = "Disk není připojen – raw restore možný",
                    IsSelected = false
                });
            }
        }
        catch { }

        await Task.CompletedTask;
    }

    private void SelectBackup()
    {
        // Selection is handled by UI binding to SelectedBackup
        // When user clicks a backup in the list, it sets SelectedBackup
        if (SelectedBackup != null)
        {
            HasSelectedBackup = true;
            StatusMessage = $"Vybrána záloha: {SelectedBackup.SourceModel} ({SelectedBackup.BackupDate}) — {SelectedBackup.TotalBytesText}";
            _ = ScanTargetDisksAsync();
        }
    }

    private async Task StartRestoreAsync()
    {
        if (SelectedBackup == null || Phase != RestorePhase.Ready) return;

        var targetDisk = TargetDisks.FirstOrDefault(t => t.IsSelected);
        if (targetDisk == null)
        {
            await _dialogService.ShowErrorAsync("Chyba", "Není vybrán cílový disk pro obnovu.");
            return;
        }

        // Extra warning for source disk
        if (targetDisk.IsSourceDisk)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "⚠️ VAROVÁNÍ – Původní disk",
                $"Vybraný cílový disk je PŮVODNÍ disk, ze kterého byla záloha vytvořena!\n\n" +
                $"Obnova na tento disk PŘEPÍŠE všechna data na něm.\n\n" +
                $"OPRAVDU chcete pokračovat?");
            if (!confirmed) return;
        }

        // General confirmation
        var generalConfirm = await _dialogService.ShowConfirmationAsync(
            "💾 Spustit obnovu",
            $"Obnova bude provedena na disk: {targetDisk.DisplayName}\n\n" +
            $"Zdrojová záloha: {SelectedBackup.SourceModel}\n" +
            $"Datum zálohy: {SelectedBackup.BackupDate}\n" +
            $"Režim: {SelectedBackup.Mode}\n" +
            $"Data: {SelectedBackup.TotalBytesText}\n\n" +
            $"OPRAVDU SPUSTIT OBNOVU?");
        if (!generalConfirm) return;

        _restoreCancellation = new CancellationTokenSource();
        _restoreStartTime = DateTime.UtcNow;
        _totalBytesToRestore = SelectedBackup.TotalBytes;
        _totalBytesRestored = 0;
        _restoreLog.Clear();
        Phase = RestorePhase.Running;
        StatusMessage = "Obnova běží...";

        try
        {
            if (SelectedBackup.Mode == "Raw obraz")
            {
                await RunRawRestoreAsync(targetDisk.DrivePath, _restoreCancellation.Token);
            }
            else
            {
                await RunFileRestoreAsync(targetDisk.DrivePath, _restoreCancellation.Token);
            }

            // Verification phase
            Phase = RestorePhase.Verifying;
            StatusMessage = "Ověřuji obnovená data...";
            await VerifyRestoreAsync(targetDisk.DrivePath, _restoreCancellation.Token);

            Phase = RestorePhase.Completed;
            StatusMessage = IsVerifyOk
                ? "✅ Obnova dokončena a ověřena!"
                : "⚠️ Obnova dokončena, ale ověření našlo nesrovnalosti.";
            OverallProgress = 100;
        }
        catch (OperationCanceledException)
        {
            Phase = RestorePhase.Cancelled;
            StatusMessage = "Obnova přerušena uživatelem.";
        }
        catch (Exception ex)
        {
            Phase = RestorePhase.Failed;
            StatusMessage = $"Obnova selhala: {ex.Message}";
            _restoreLog.Add($"[ERROR] {ex.Message}");
        }
        finally
        {
            _restoreCancellation?.Dispose();
            _restoreCancellation = null;
            RestoreLogText = string.Join("\n", _restoreLog);
        }
    }

    private async Task RunFileRestoreAsync(string targetRoot, CancellationToken ct)
    {
        if (SelectedBackup == null) return;

        _restoreLog.Add($"[{DateTime.Now:HH:mm:ss}] Zahajuji souborovou obnovu do: {targetRoot}");

        // Find the backup data directory (first part)
        var backupDataDir = SelectedBackup.BackupRoot;
        if (!Directory.Exists(backupDataDir))
            throw new DirectoryNotFoundException($"Složka zálohy nenalezena: {backupDataDir}");

        // Also check for multi-part backups
        var allParts = new List<string> { backupDataDir };
        var parentDir = Path.GetDirectoryName(backupDataDir);
        if (parentDir != null)
        {
            var baseName = Path.GetFileName(backupDataDir);
            var partDirs = Directory.GetDirectories(parentDir, baseName + "_part*");
            allParts.AddRange(partDirs.OrderBy(d => d));
        }

        foreach (var partDir in allParts)
        {
            ct.ThrowIfCancellationRequested();
            _restoreLog.Add($"[{DateTime.Now:HH:mm:ss}] Zpracovávám část: {partDir}");

            var dirs = Directory.GetDirectories(partDir);
            foreach (var sourceDir in dirs)
            {
                ct.ThrowIfCancellationRequested();
                var dirName = new DirectoryInfo(sourceDir).Name;
                var destDir = Path.Combine(targetRoot, dirName);
                Directory.CreateDirectory(destDir);

                _restoreLog.Add($"[{DateTime.Now:HH:mm:ss}] Obnovuji: {sourceDir} → {destDir}");
                await CopyDirectoryForRestoreAsync(sourceDir, destDir, ct);
            }
        }

        _restoreLog.Add($"[{DateTime.Now:HH:mm:ss}] Souborová obnova dokončena.");
    }

    private async Task CopyDirectoryForRestoreAsync(string sourceDir, string destDir, CancellationToken ct)
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
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            var fileInfo = new FileInfo(file);
            var fileSize = fileInfo.Length;

            CurrentFileName = relativePath;
            CurrentFileBytes = fileSize;
            CurrentFileProgress = 0;

            await CopyFileWithProgressAsync(file, destPath, fileSize, ct);

            _totalBytesRestored += fileSize;
            OverallProgress = _totalBytesToRestore > 0
                ? (double)_totalBytesRestored / _totalBytesToRestore * 100
                : 0;

            UpdateSpeedAndEta();
        }
    }

    private async Task RunRawRestoreAsync(string targetPath, CancellationToken ct)
    {
        if (SelectedBackup == null) return;

        _restoreLog.Add($"[{DateTime.Now:HH:mm:ss}] Zahajuji raw obnovu na: {targetPath}");

        // Find all raw image parts
        var allParts = new List<string>();
        var part1 = Path.Combine(SelectedBackup.BackupRoot, "disk_image_part1.raw");
        if (File.Exists(part1))
        {
            allParts.Add(part1);
            for (int i = 2; ; i++)
            {
                var partN = Path.Combine(SelectedBackup.BackupRoot, $"disk_image_part{i}.raw");
                if (File.Exists(partN))
                    allParts.Add(partN);
                else
                    break;
            }
        }
        else
        {
            // Try single image file
            var singleImg = Path.Combine(SelectedBackup.BackupRoot, "disk_image.raw");
            if (File.Exists(singleImg))
                allParts.Add(singleImg);
        }

        if (allParts.Count == 0)
            throw new FileNotFoundException("Raw obraz nenalezen v záloze.");

        const int bufferSize = 1024 * 1024; // 1MB buffer
        var buffer = new byte[bufferSize];

        using var targetStream = new FileStream(targetPath, FileMode.Open, FileAccess.Write,
            FileShare.None, bufferSize, FileOptions.SequentialScan);

        foreach (var partPath in allParts)
        {
            ct.ThrowIfCancellationRequested();
            _restoreLog.Add($"[{DateTime.Now:HH:mm:ss}] Zapisuji část: {partPath}");

            var partInfo = new FileInfo(partPath);
            CurrentFileName = Path.GetFileName(partPath);
            CurrentFileBytes = partInfo.Length;
            CurrentFileProgress = 0;

            using var sourceStream = new FileStream(partPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, bufferSize, FileOptions.SequentialScan);

            long partBytesRead = 0;
            while (partBytesRead < partInfo.Length)
            {
                ct.ThrowIfCancellationRequested();

                int bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, bufferSize), ct);
                if (bytesRead == 0) break;

                await targetStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                partBytesRead += bytesRead;
                _totalBytesRestored += bytesRead;

                CurrentFileProgress = partInfo.Length > 0
                    ? (double)partBytesRead / partInfo.Length * 100
                    : 0;

                OverallProgress = _totalBytesToRestore > 0
                    ? (double)_totalBytesRestored / _totalBytesToRestore * 100
                    : 0;

                UpdateSpeedAndEta();
            }
        }

        await targetStream.FlushAsync(ct);
        _restoreLog.Add($"[{DateTime.Now:HH:mm:ss}] Raw obnova dokončena.");
    }

    private async Task VerifyRestoreAsync(string targetRoot, CancellationToken ct)
    {
        if (SelectedBackup == null) return;

        _restoreLog.Add($"[{DateTime.Now:HH:mm:ss}] Zahajuji ověření...");
        VerifyResult = "Ověřování...";
        IsVerifyOk = false;

        try
        {
            if (SelectedBackup.Mode == "Raw obraz")
            {
                await VerifyRawRestoreAsync(targetRoot, ct);
            }
            else
            {
                await VerifyFileRestoreAsync(targetRoot, ct);
            }
        }
        catch (Exception ex)
        {
            VerifyResult = $"Ověření selhalo: {ex.Message}";
            IsVerifyOk = false;
        }
    }

    private async Task VerifyFileRestoreAsync(string targetRoot, CancellationToken ct)
    {
        if (SelectedBackup == null) return;

        long filesChecked = 0;
        long filesOk = 0;
        long filesMissing = 0;
        long filesSizeMismatch = 0;
        var mismatchedFiles = new List<string>();

        var allParts = new List<string> { SelectedBackup.BackupRoot };
        var parentDir = Path.GetDirectoryName(SelectedBackup.BackupRoot);
        if (parentDir != null)
        {
            var baseName = Path.GetFileName(SelectedBackup.BackupRoot);
            var partDirs = Directory.GetDirectories(parentDir, baseName + "_part*");
            allParts.AddRange(partDirs.OrderBy(d => d));
        }

        foreach (var partDir in allParts)
        {
            ct.ThrowIfCancellationRequested();

            var dirs = Directory.GetDirectories(partDir);
            foreach (var sourceDir in dirs)
            {
                var dirName = new DirectoryInfo(sourceDir).Name;
                var destDir = Path.Combine(targetRoot, dirName);

                if (!Directory.Exists(destDir))
                {
                    _restoreLog.Add($"[VERIFY] Chybějící složka: {destDir}");
                    continue;
                }

                var sourceFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
                foreach (var sourceFile in sourceFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    filesChecked++;

                    var relativePath = Path.GetRelativePath(sourceDir, sourceFile);
                    var destFile = Path.Combine(destDir, relativePath);

                    if (!File.Exists(destFile))
                    {
                        filesMissing++;
                        mismatchedFiles.Add($"CHYBÍ: {relativePath}");
                        continue;
                    }

                    var sourceInfo = new FileInfo(sourceFile);
                    var destInfo = new FileInfo(destFile);

                    if (sourceInfo.Length != destInfo.Length)
                    {
                        filesSizeMismatch++;
                        mismatchedFiles.Add($"VELIKOST: {relativePath} ({sourceInfo.Length} vs {destInfo.Length})");
                        continue;
                    }

                    filesOk++;
                }
            }
        }

        var totalIssues = filesMissing + filesSizeMismatch;
        IsVerifyOk = totalIssues == 0;

        VerifyResult = IsVerifyOk
            ? $"✅ Vše OK — {filesOk} souborů ověřeno"
            : $"⚠️ Problémy: {filesMissing} chybí, {filesSizeMismatch} nesouhlasí velikostí, {filesOk} OK";

        _restoreLog.Add($"[VERIFY] Zkontrolováno: {filesChecked}, OK: {filesOk}, Chybí: {filesMissing}, Velikost: {filesSizeMismatch}");

        if (mismatchedFiles.Count > 0 && mismatchedFiles.Count <= 20)
        {
            foreach (var mf in mismatchedFiles)
                _restoreLog.Add($"[VERIFY] {mf}");
        }
    }

    private async Task VerifyRawRestoreAsync(string targetPath, CancellationToken ct)
    {
        if (SelectedBackup == null) return;

        // For raw restore, verify that the target size matches
        try
        {
            var targetInfo = new FileInfo(targetPath);
            if (!targetInfo.Exists)
            {
                VerifyResult = "❌ Cílový soubor nenalezen.";
                IsVerifyOk = false;
                return;
            }

            // Sum up all raw parts
            long totalSourceBytes = 0;
            var allParts = new List<string>();
            var part1 = Path.Combine(SelectedBackup.BackupRoot, "disk_image_part1.raw");
            if (File.Exists(part1))
            {
                for (int i = 1; ; i++)
                {
                    var partN = Path.Combine(SelectedBackup.BackupRoot, $"disk_image_part{i}.raw");
                    if (File.Exists(partN))
                        allParts.Add(partN);
                    else
                        break;
                }
            }
            else
            {
                var singleImg = Path.Combine(SelectedBackup.BackupRoot, "disk_image.raw");
                if (File.Exists(singleImg))
                    allParts.Add(singleImg);
            }

            foreach (var part in allParts)
                totalSourceBytes += new FileInfo(part).Length;

            if (targetInfo.Length >= totalSourceBytes)
            {
                IsVerifyOk = true;
                VerifyResult = $"✅ Raw obraz ověřen — {FormatBytesLong(targetInfo.Length)} zapsáno";
            }
            else
            {
                IsVerifyOk = false;
                VerifyResult = $"⚠️ Velikost nesouhlasí — zdroj: {FormatBytesLong(totalSourceBytes)}, cíl: {FormatBytesLong(targetInfo.Length)}";
            }
        }
        catch (Exception ex)
        {
            VerifyResult = $"Ověření selhalo: {ex.Message}";
            IsVerifyOk = false;
        }

        await Task.CompletedTask;
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

    private void UpdateSpeedAndEta()
    {
        var elapsed = DateTime.UtcNow - _restoreStartTime;
        ElapsedTime = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

        if (elapsed.TotalSeconds > 1 && _totalBytesRestored > 0)
        {
            var speedBps = _totalBytesRestored / elapsed.TotalSeconds;
            CurrentSpeed = FormatBytesLong((long)speedBps) + "/s";

            if (_totalBytesToRestore > 0 && speedBps > 0)
            {
                var remainingBytes = _totalBytesToRestore - _totalBytesRestored;
                var remainingSeconds = remainingBytes / speedBps;
                var remaining = TimeSpan.FromSeconds(remainingSeconds);
                EstimatedRemaining = remaining.TotalHours >= 1
                    ? $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
                    : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
        }
    }

    private void CancelRestore()
    {
        _restoreCancellation?.Cancel();
        StatusMessage = "Přerušuji obnovu...";
    }

    private void GoBack()
    {
        if (Phase == RestorePhase.Running || Phase == RestorePhase.Verifying)
        {
            _ = _dialogService.ShowErrorAsync("Obnova běží", "Nelze opustit během obnovy. Nejprve obnovu přerušte.");
            return;
        }
        _navigationService.NavigateTo<BackupViewModel>();
    }

    private static string FormatBytesLong(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _restoreCancellation?.Cancel();
        _restoreCancellation?.Dispose();

        GC.SuppressFinalize(this);
    }
}
