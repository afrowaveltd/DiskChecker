using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace DiskChecker.TUI.Services;

/// <summary>Backup strategy offered by the terminal backup screen.</summary>
public enum TuiBackupMode
{
    /// <summary>Copy a chosen directory tree to the target volume.</summary>
    FileLevel = 0,

    /// <summary>Byte-for-byte sector image of the whole device.</summary>
    RawImage = 1,

    /// <summary>Fixed VHDx image of the whole device.</summary>
    VhdxImage = 2,
}

/// <summary>Provides localized text for the progress output produced by the image service.</summary>
/// <param name="key">Locale key.</param>
/// <param name="args">Optional format arguments.</param>
public delegate string TuiLocalizer(string key, params object[] args);

/// <summary>A writable volume that can receive a backup.</summary>
public sealed record TuiBackupTarget(string Root, string Label, long FreeBytes, long TotalBytes)
{
    /// <summary>True when <paramref name="requiredBytes"/> fits while keeping the system reserve.</summary>
    public bool Fits(long requiredBytes, long reserveBytes) => FreeBytes - reserveBytes >= requiredBytes;

    /// <summary>Free space usable for a backup once the system reserve is kept.</summary>
    public long UsableBytes(long reserveBytes) => Math.Max(0, FreeBytes - reserveBytes);
}

/// <summary>Everything the image service needs to create a backup.</summary>
public sealed class TuiBackupRequest
{
    /// <summary>Device node or image source path (opened read-only, never for write).</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>Human readable source model shown in the manifest.</summary>
    public string SourceModel { get; init; } = string.Empty;

    /// <summary>Number of bytes to read; zero means "the length of <see cref="SourcePath"/>".</summary>
    public long TotalSize { get; init; }

    /// <summary>Backup strategy.</summary>
    public TuiBackupMode Mode { get; init; } = TuiBackupMode.RawImage;

    /// <summary>Directory on the target volume that receives the backup folder.</summary>
    public string TargetRoot { get; init; } = string.Empty;

    /// <summary>Source directory copied in <see cref="TuiBackupMode.FileLevel"/> mode.</summary>
    public string? SourceDirectory { get; init; }

    /// <summary>Estimated payload of the file level copy; used for progress only.</summary>
    public long EstimatedBytes { get; init; }
}

/// <summary>Outcome of a completed backup.</summary>
public sealed class TuiBackupResult
{
    public string BackupRoot { get; init; } = string.Empty;

    public string ManifestPath { get; init; } = string.Empty;

    public string? ImagePath { get; init; }

    public long TotalBytes { get; init; }

    public long UnreadableBytes { get; init; }

    public int Parts { get; init; } = 1;

    public TuiBackupMode Mode { get; init; }

    public bool Mountable { get; init; }

    public string? Sha256 { get; init; }

    /// <summary>True when the written image was read back and matched the streamed SHA-256.</summary>
    public bool Verified { get; init; }
}

/// <summary>Request to write an image back onto a target disk or file.</summary>
public sealed class TuiRestoreRequest
{
    /// <summary>Image part files in write order; the VHDx header is skipped by <see cref="DataStartOffset"/>.</summary>
    public IReadOnlyList<string> ImagePaths { get; init; } = Array.Empty<string>();

    /// <summary>Destination device path or regular file.</summary>
    public string TargetPath { get; init; } = string.Empty;

    /// <summary>Number of payload bytes to write.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Offset inside the first part image where the payload starts (VHDx header/metadata).</summary>
    public long DataStartOffset { get; init; }

    /// <summary>Expected payload checksum from the manifest, when present.</summary>
    public string? ExpectedSha256 { get; init; }
}

/// <summary>Outcome of a restore.</summary>
public sealed class TuiRestoreResult
{
    public long BytesWritten { get; init; }

    public string? Sha256 { get; init; }

    /// <summary>True when a manifest checksum existed and matched the written bytes.</summary>
    public bool Verified { get; init; }

    public string? Message { get; init; }
}

/// <summary>Contents of <c>backup_manifest.json</c>.</summary>
public sealed class TuiBackupManifest
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public string? SourceDrive { get; set; }

    public string? SourceModel { get; set; }

    public string BackupDate { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string ImageFormat { get; set; } = string.Empty;

    public string? MountableImage { get; set; }

    public long TotalBytes { get; set; }

    public int BlockSize { get; set; } = TuiImageBackupService.BlockSize;

    public long UnreadableBytes { get; set; }

    public int Parts { get; set; } = 1;

    public List<string> PartFiles { get; set; } = new();

    public string? Sha256 { get; set; }

    public long DataStartOffset { get; set; }

    public long VirtualDiskBytes { get; set; }

    public List<string> Folders { get; set; } = new();

    public List<string> Files { get; set; } = new();

    public string? Note { get; set; }

    /// <summary>Serializes the manifest next to the image.</summary>
    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, JsonSerializer.Serialize(this, WriteOptions));
    }

    /// <summary>Reads a manifest, returning null when the file is missing or malformed.</summary>
    public static TuiBackupManifest? Read(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<TuiBackupManifest>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>A backup folder discovered on a visible volume.</summary>
public sealed record TuiDiscoveredBackup(
    string ManifestPath,
    string BackupRoot,
    string SourceDrive,
    string SourceModel,
    string BackupDate,
    string Mode,
    long TotalBytes,
    int Parts,
    long UnreadableBytes,
    string? Sha256,
    long DataStartOffset,
    string? ImagePath);

/// <summary>
/// Streaming backup and restore of whole disks in the terminal UI. The source device is
/// always opened read-only with <see cref="FileShare.Read"/>; the only writer is
/// <see cref="RestoreAsync"/>, which the restore screen guards with a double confirmation.
/// </summary>
public sealed class TuiImageBackupService
{
    /// <summary>I/O block size shared with the desktop application.</summary>
    public const int BlockSize = 1024 * 1024;

    /// <summary>Free space deliberately kept on the target volume (4 GiB).</summary>
    public const long SystemReserveBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>A device that returns more unreadable blocks in a row than this is considered broken.</summary>
    public const int MaxConsecutiveUnreadableBlocks = 64;

    private static readonly TuiLocalizer DefaultLocalizer = (key, _) => key;

    private readonly TuiLocalizer _localize;

    public TuiImageBackupService(TuiLocalizer? localize = null) => _localize = localize ?? DefaultLocalizer;

    /// <summary>Volume <c>Root</c> where backups are written by default.</summary>
    public static string BackupFolderPrefix(TuiBackupMode mode) => mode switch
    {
        TuiBackupMode.VhdxImage => "DiskChecker_VhdxBackup",
        TuiBackupMode.RawImage => "DiskChecker_RawBackup",
        _ => "DiskChecker_Backup",
    };

    /// <summary>Bytes a backup of <paramref name="sourceBytes"/> needs on the target volume.</summary>
    public static long GetRequiredBytes(long sourceBytes, TuiBackupMode mode)
    {
        if (sourceBytes <= 0)
        {
            return 0;
        }

        return mode == TuiBackupMode.VhdxImage
            ? TuiVhdxImageUtility.CalculateRequiredFileBytes(sourceBytes)
            : sourceBytes;
    }

    /// <summary>Writable volumes offered as backup targets (ready, not optical, not the source).</summary>
    public static IReadOnlyList<TuiBackupTarget> GetWritableTargets(string? excludedRoot = null)
    {
        var targets = new List<TuiBackupTarget>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType == DriveType.CDRom || drive.AvailableFreeSpace <= 0)
                {
                    continue;
                }

                string root = drive.RootDirectory.FullName;
                if (!string.IsNullOrEmpty(excludedRoot) &&
                    root.StartsWith(excludedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? root : $"{root} ({drive.VolumeLabel})";
                targets.Add(new TuiBackupTarget(root, label, drive.AvailableFreeSpace, drive.TotalSize));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A removable target may disappear during discovery; skip it.
            }
        }

        return targets.OrderByDescending(target => target.FreeBytes).ToArray();
    }

    /// <summary>Free bytes available for <paramref name="path"/>, or null when it cannot be determined.</summary>
    public static long? GetFreeSpaceAt(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? candidate;
        try
        {
            candidate = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        for (int depth = 0; depth < 24 && !string.IsNullOrEmpty(candidate); depth++)
        {
            try
            {
                var drive = new DriveInfo(candidate);
                if (drive.IsReady)
                {
                    return drive.AvailableFreeSpace;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Not a mount point: fall through to the parent directory.
            }

            string trimmed = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0)
            {
                break;
            }

            string? parent = Path.GetDirectoryName(trimmed);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, candidate, StringComparison.Ordinal))
            {
                break;
            }

            candidate = parent;
        }

        return null;
    }

    /// <summary>Throws when <paramref name="requiredBytes"/> does not fit on the target volume.</summary>
    public static void EnsureSpace(string targetRoot, long requiredBytes)
    {
        long? free = GetFreeSpaceAt(targetRoot);
        if (free is null || requiredBytes <= 0)
        {
            return;
        }

        long usable = free.Value - SystemReserveBytes;
        if (usable >= requiredBytes)
        {
            return;
        }

        throw new IOException(
            $"Na cílovém disku není dost místa. Potřeba {TuiFormats.DecimalSize(requiredBytes)}, " +
            $"k dispozici {TuiFormats.DecimalSize(usable)} po odečtení rezervy " +
            $"{TuiFormats.DecimalSize(SystemReserveBytes)}.");
    }

    /// <summary>Finds backups in <c>DiskChecker_*Backup_*</c> folders containing a manifest.</summary>
    public static IReadOnlyList<TuiDiscoveredBackup> ScanBackups(IEnumerable<string>? roots = null)
    {
        var searchRoots = roots?.Where(root => !string.IsNullOrWhiteSpace(root)).ToArray()
            ?? DefaultScanRoots();

        var found = new List<TuiDiscoveredBackup>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in searchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.GetDirectories(root, "DiskChecker_*Backup_*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            foreach (string dir in candidates)
            {
                var backup = ReadBackup(dir);
                if (backup is not null && seen.Add(backup.ManifestPath))
                {
                    found.Add(backup);
                }
            }
        }

        return found
            .OrderByDescending(backup => backup.BackupDate, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Reads one backup folder; returns null when it holds no usable manifest.</summary>
    public static TuiDiscoveredBackup? ReadBackup(string backupRoot)
    {
        if (string.IsNullOrWhiteSpace(backupRoot))
        {
            return null;
        }

        string manifestPath = Path.Combine(backupRoot, "backup_manifest.json");
        var manifest = TuiBackupManifest.Read(manifestPath);
        if (manifest is null)
        {
            return null;
        }

        string? imagePath = ResolveImagePath(backupRoot, manifest);
        return new TuiDiscoveredBackup(
            manifestPath,
            backupRoot,
            manifest.SourceDrive ?? "?",
            manifest.SourceModel ?? "?",
            manifest.BackupDate,
            manifest.Mode,
            manifest.TotalBytes > 0 ? manifest.TotalBytes : manifest.VirtualDiskBytes,
            manifest.Parts,
            manifest.UnreadableBytes,
            manifest.Sha256,
            manifest.DataStartOffset,
            imagePath);
    }

    /// <summary>Image part files of a discovered backup, in write order.</summary>
    public static IReadOnlyList<string> ResolveImagePaths(TuiDiscoveredBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        if (string.IsNullOrEmpty(backup.ImagePath) || !File.Exists(backup.ImagePath))
        {
            return Array.Empty<string>();
        }

        return new[] { backup.ImagePath };
    }

    /// <summary>Creates a backup according to <paramref name="request"/>.</summary>
    public async Task<TuiBackupResult> RunAsync(TuiBackupRequest request, TuiProgress? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Mode switch
        {
            TuiBackupMode.FileLevel => await RunFileLevelAsync(request, progress, cancellationToken).ConfigureAwait(false),
            TuiBackupMode.VhdxImage => await RunImageAsync(request, isVhdx: true, progress, cancellationToken).ConfigureAwait(false),
            _ => await RunImageAsync(request, isVhdx: false, progress, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>Writes an image back onto a target device or file.</summary>
    public async Task<TuiRestoreResult> RestoreAsync(TuiRestoreRequest request, TuiProgress? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ImagePaths.Count == 0)
        {
            throw new FileNotFoundException("Obraz zálohy nebyl nalezen.");
        }

        var parts = request.ImagePaths.Where(File.Exists).ToArray();
        if (parts.Length == 0)
        {
            throw new FileNotFoundException("Obraz zálohy nebyl nalezen.");
        }

        long totalBytes = request.TotalBytes > 0
            ? request.TotalBytes
            : parts.Sum(part => Math.Max(0, new FileInfo(part).Length - request.DataStartOffset));

        bool blockDevice = IsBlockDeviceTarget(request.TargetPath);
        if (!blockDevice)
        {
            string? directory = Path.GetDirectoryName(request.TargetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var sizing = new FileStream(request.TargetPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 4096, FileOptions.SequentialScan);
            if (sizing.Length < totalBytes)
            {
                sizing.SetLength(totalBytes);
            }
        }

        if (progress is not null)
        {
            progress.Phase = _localize("Tui.Restore.Phase.Writing");
        }

        var startedUtc = DateTime.UtcNow;
        var buffer = new byte[BlockSize];
        long written = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        using (var target = new FileStream(
                   request.TargetPath,
                   blockDevice ? FileMode.Open : FileMode.OpenOrCreate,
                   FileAccess.Write,
                   FileShare.None,
                   BlockSize,
                   FileOptions.SequentialScan))
        {
            for (int index = 0; index < parts.Length; index++)
            {
                long remaining = totalBytes - written;
                if (remaining <= 0)
                {
                    break;
                }

                long skip = index == 0 ? request.DataStartOffset : 0;
                using var source = new FileStream(parts[index], FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize, FileOptions.SequentialScan);
                if (skip > 0)
                {
                    source.Position = Math.Min(skip, source.Length);
                }

                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int toRead = (int)Math.Min(BlockSize, remaining);
                    int read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    written += read;
                    remaining -= read;
                    Report(progress, written, totalBytes, startedUtc, "Tui.Restore.Metric.Blocks", written / BlockSize, totalBytes / BlockSize);
                }
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        string actual = Convert.ToHexString(hash.GetHashAndReset());
        bool verified = !string.IsNullOrWhiteSpace(request.ExpectedSha256) &&
                        string.Equals(actual, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase);

        string message;
        if (string.IsNullOrWhiteSpace(request.ExpectedSha256))
        {
            message = _localize("Tui.Restore.Log.NoChecksum");
        }
        else if (verified)
        {
            message = _localize("Tui.Restore.Log.Verified");
        }
        else
        {
            message = _localize("Tui.Restore.Log.Mismatch");
        }

        if (progress is not null)
        {
            progress.Percent = 100;
            progress.Phase = _localize("Tui.Restore.Phase.Done");
            progress.Detail = TuiFormats.DecimalSize(written);
            progress.AddLog(_localize("Tui.Restore.Log.Done", TuiFormats.DecimalSize(written)));
            progress.AddLog(message);
            progress.UpdateFromElapsed(startedUtc);
        }

        return new TuiRestoreResult
        {
            BytesWritten = written,
            Sha256 = actual,
            Verified = verified,
            Message = message,
        };
    }

    private static IReadOnlyList<string> DefaultScanRoots()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && drive.DriveType != DriveType.CDRom)
                {
                    roots.Add(drive.RootDirectory.FullName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Unreadable volumes are simply not searched.
            }
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            roots.Add(home);
        }

        return roots;
    }

    private static string? ResolveImagePath(string backupRoot, TuiBackupManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.MountableImage))
        {
            if (File.Exists(manifest.MountableImage))
            {
                return manifest.MountableImage;
            }

            string local = Path.Combine(backupRoot, Path.GetFileName(manifest.MountableImage));
            if (File.Exists(local))
            {
                return local;
            }
        }

        foreach (string name in new[] { "disk_image.img", "disk_image.vhdx", "disk_image.raw" })
        {
            string candidate = Path.Combine(backupRoot, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (string pattern in new[] { "disk_image.img.part*", "disk_image.vhdx.part*" })
        {
            try
            {
                var parts = Directory.GetFiles(backupRoot, pattern, SearchOption.TopDirectoryOnly);
                if (parts.Length > 0)
                {
                    return parts.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).First();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Ignore unreadable part folders.
            }
        }

        return null;
    }

    private static bool IsBlockDeviceTarget(string path) =>
        path.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("PhysicalDrive", StringComparison.OrdinalIgnoreCase);

    private async Task<TuiBackupResult> RunImageAsync(
        TuiBackupRequest request,
        bool isVhdx,
        TuiProgress? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !File.Exists(request.SourcePath))
        {
            throw new FileNotFoundException("Zdrojový disk nebyl nalezen.", request.SourcePath);
        }

        long totalSize = request.TotalSize > 0 ? request.TotalSize : new FileInfo(request.SourcePath).Length;
        if (totalSize <= 0)
        {
            throw new InvalidOperationException("Zdrojový disk hlásí nulovou velikost — zálohu nelze vytvořit.");
        }

        EnsureSpace(request.TargetRoot, GetRequiredBytes(totalSize, isVhdx ? TuiBackupMode.VhdxImage : TuiBackupMode.RawImage));

        string backupRoot = Path.Combine(
            request.TargetRoot,
            BackupFolderPrefix(isVhdx ? TuiBackupMode.VhdxImage : TuiBackupMode.RawImage) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(backupRoot);

        string imagePath = Path.Combine(backupRoot, isVhdx ? "disk_image.vhdx" : "disk_image.img");
        long dataStartOffset = 0;
        if (isVhdx)
        {
            dataStartOffset = TuiVhdxImageUtility.CalculateDataStartOffset(totalSize);
            await TuiVhdxImageUtility.WriteFixedHeaderAsync(imagePath, totalSize, cancellationToken).ConfigureAwait(false);
        }

        if (progress is not null)
        {
            progress.Phase = _localize("Tui.Backup.Phase.Streaming");
            progress.Detail = request.SourcePath;
        }

        var startedUtc = DateTime.UtcNow;
        var buffer = new byte[BlockSize];
        long bytesRead = 0;
        long unreadableBytes = 0;
        int consecutiveErrors = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // The source device is opened read-only: the backup path never writes to it.
        using (var device = new FileStream(request.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize, FileOptions.SequentialScan))
        using (var image = new FileStream(
                   imagePath,
                   isVhdx ? FileMode.Open : FileMode.Create,
                   FileAccess.ReadWrite,
                   FileShare.Read,
                   BlockSize,
                   FileOptions.SequentialScan))
        {
            while (bytesRead < totalSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int toRead = (int)Math.Min(BlockSize, totalSize - bytesRead);
                int readNow = 0;
                bool readable = true;

                try
                {
                    readNow = await device.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    readable = false;
                }

                if (!readable || readNow == 0)
                {
                    Array.Clear(buffer, 0, toRead);
                    readNow = toRead;
                    unreadableBytes += toRead;
                    consecutiveErrors++;

                    if (consecutiveErrors == 1 && progress is not null)
                    {
                        progress.AddLog(_localize("Tui.Backup.Log.Unreadable", TuiFormats.DecimalSize(bytesRead)));
                    }

                    if (consecutiveErrors >= MaxConsecutiveUnreadableBlocks)
                    {
                        throw new IOException(
                            $"Příliš mnoho nečitelných bloků za sebou ({consecutiveErrors}) — disk je pravděpodobně vážně poškozen. Záloha přerušena.");
                    }
                }
                else
                {
                    consecutiveErrors = 0;
                }

                image.Position = dataStartOffset + bytesRead;
                await image.WriteAsync(buffer.AsMemory(0, readNow), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, readNow);

                bytesRead += readNow;
                Report(progress, bytesRead, totalSize, startedUtc, "Tui.Backup.Metric.Blocks", bytesRead / BlockSize, totalSize / BlockSize, unreadableBytes);
            }

            await image.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        string sha256 = Convert.ToHexString(hash.GetHashAndReset());

        if (progress is not null)
        {
            progress.Phase = _localize("Tui.Backup.Phase.Verifying");
            progress.Detail = imagePath;
        }

        bool verified = await VerifyImageAsync(imagePath, dataStartOffset, totalSize, sha256, cancellationToken).ConfigureAwait(false);
        if (progress is not null)
        {
            progress.AddLog(verified ? _localize("Tui.Backup.Log.Verified") : _localize("Tui.Backup.Log.VerifyMismatch"));
        }

        var manifest = new TuiBackupManifest
        {
            SourceDrive = request.SourcePath,
            SourceModel = request.SourceModel,
            BackupDate = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
            Mode = isVhdx ? "VhdxImage" : "RawImage",
            ImageFormat = isVhdx ? "VHDx Fixed (mountable)" : "Raw mountable IMG",
            MountableImage = imagePath,
            TotalBytes = bytesRead,
            BlockSize = BlockSize,
            UnreadableBytes = unreadableBytes,
            Parts = 1,
            PartFiles = new List<string> { Path.GetFileName(imagePath) },
            Sha256 = sha256,
            DataStartOffset = dataStartOffset,
            VirtualDiskBytes = totalSize,
            Note = isVhdx
                ? "Fixed VHDx obraz disku. Připojení: qemu-nbd -c /dev/nbd0 disk_image.vhdx"
                : "Soubor disk_image.img je byte-for-byte obraz disku.",
        };

        string manifestPath = Path.Combine(backupRoot, "backup_manifest.json");
        manifest.Write(manifestPath);

        if (progress is not null)
        {
            progress.Percent = 100;
            progress.Phase = _localize("Tui.Backup.Phase.Done");
            progress.Detail = imagePath;
            progress.AddLog(_localize("Tui.Backup.Log.Done", TuiFormats.DecimalSize(bytesRead)));
            progress.UpdateFromElapsed(startedUtc);
        }

        return new TuiBackupResult
        {
            BackupRoot = backupRoot,
            ManifestPath = manifestPath,
            ImagePath = imagePath,
            TotalBytes = bytesRead,
            UnreadableBytes = unreadableBytes,
            Parts = 1,
            Mode = isVhdx ? TuiBackupMode.VhdxImage : TuiBackupMode.RawImage,
            Mountable = true,
            Sha256 = sha256,
            Verified = verified,
        };
    }

    private async Task<TuiBackupResult> RunFileLevelAsync(
        TuiBackupRequest request,
        TuiProgress? progress,
        CancellationToken cancellationToken)
    {
        string sourceDirectory = request.SourceDirectory ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException("Zdrojová složka pro souborovou zálohu nebyla nalezena: " + sourceDirectory);
        }

        string backupRoot = Path.Combine(
            request.TargetRoot,
            BackupFolderPrefix(TuiBackupMode.FileLevel) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(backupRoot);

        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
        long totalBytes = request.EstimatedBytes > 0
            ? request.EstimatedBytes
            : files.Sum(file => new FileInfo(file).Length);

        EnsureSpace(request.TargetRoot, totalBytes);

        if (progress is not null)
        {
            progress.Phase = _localize("Tui.Backup.Phase.Files");
            progress.Detail = sourceDirectory;
        }

        var startedUtc = DateTime.UtcNow;
        var buffer = new byte[BlockSize];
        long copied = 0;

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relative = Path.GetRelativePath(sourceDirectory, file);
            string destination = Path.Combine(backupRoot, relative);
            string? directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize, FileOptions.SequentialScan);
            using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, BlockSize, FileOptions.SequentialScan);

            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
                Report(progress, copied, totalBytes, startedUtc, "Tui.Backup.Metric.Files", copied / BlockSize, totalBytes / BlockSize);
            }
        }

        string manifestPath = Path.Combine(backupRoot, "backup_manifest.json");
        var manifest = new TuiBackupManifest
        {
            SourceDrive = request.SourcePath,
            SourceModel = request.SourceModel,
            BackupDate = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
            Mode = "FileLevel",
            ImageFormat = "File level copy",
            MountableImage = null,
            TotalBytes = copied,
            BlockSize = BlockSize,
            UnreadableBytes = 0,
            Parts = 1,
            PartFiles = new List<string>(),
            Folders = new List<string> { sourceDirectory },
            Files = files.Select(file => Path.GetRelativePath(sourceDirectory, file)).ToList(),
            Sha256 = null,
            Note = "Souborová záloha složky " + sourceDirectory,
        };
        manifest.Write(manifestPath);

        if (progress is not null)
        {
            progress.Percent = 100;
            progress.Phase = _localize("Tui.Backup.Phase.Done");
            progress.Detail = backupRoot;
            progress.AddLog(_localize("Tui.Backup.Log.Done", TuiFormats.DecimalSize(copied)));
            progress.UpdateFromElapsed(startedUtc);
        }

        return new TuiBackupResult
        {
            BackupRoot = backupRoot,
            ManifestPath = manifestPath,
            ImagePath = null,
            TotalBytes = copied,
            UnreadableBytes = 0,
            Parts = 1,
            Mode = TuiBackupMode.FileLevel,
            Mountable = false,
            Sha256 = null,
            Verified = true,
        };
    }

    private static async Task<bool> VerifyImageAsync(
        string imagePath,
        long dataStartOffset,
        long totalBytes,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BlockSize];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize, FileOptions.SequentialScan);
            if (dataStartOffset > 0)
            {
                stream.Position = dataStartOffset;
            }

            long remaining = totalBytes;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int toRead = (int)Math.Min(BlockSize, remaining);
                int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return false;
                }

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        string actual = Convert.ToHexString(hash.GetHashAndReset());
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private void Report(
        TuiProgress? progress,
        long processedBytes,
        long totalBytes,
        DateTime startedUtc,
        string blockLabelKey,
        long blocksDone,
        long blocksTotal,
        long unreadableBytes = 0)
    {
        if (progress is null)
        {
            return;
        }

        double percent = totalBytes > 0 ? processedBytes * 100d / totalBytes : 0;
        var elapsed = DateTime.UtcNow - startedUtc;
        double megabytesPerSecond = elapsed.TotalSeconds > 0.2
            ? processedBytes / (1024d * 1024d) / elapsed.TotalSeconds
            : double.NaN;

        progress.Percent = percent;
        progress.SpeedText = TuiFormats.Speed(megabytesPerSecond);
        progress.Detail = TuiFormats.DecimalSize(processedBytes) + " / " + TuiFormats.DecimalSize(totalBytes);
        progress.SetMetric(_localize(blockLabelKey), $"{blocksDone} / {Math.Max(blocksTotal, 1)}");
        if (unreadableBytes > 0)
        {
            progress.Errors = (int)Math.Min(int.MaxValue, unreadableBytes / BlockSize + 1);
        }

        progress.UpdateFromElapsed(startedUtc);
    }
}