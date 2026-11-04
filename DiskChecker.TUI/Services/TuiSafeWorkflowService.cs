using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Operating mode of the safe destructive workflow. Both modes create a verified
/// raw sector image; only <see cref="RawBackupRestore"/> continues with the
/// destructive test and restores the original data afterwards.
/// </summary>
public enum TuiSafeMode
{
    /// <summary>Raw backup, verification, destructive test, restore, verification.</summary>
    RawBackupRestore,

    /// <summary>Verified raw backup only: a rescue copy, the source stays untouched.</summary>
    BackupOnly,
}

/// <summary>
/// Contents of <c>backup_manifest.json</c>. The image is only usable for a restore
/// when the recorded <see cref="Sha256"/> matches a fresh hash of the image.
/// </summary>
public sealed record TuiSafeBackupManifest
{
    /// <summary>Device path of the source that was imaged.</summary>
    public string SourceDrive { get; init; } = string.Empty;

    /// <summary>Human readable model of the source device.</summary>
    public string SourceModel { get; init; } = string.Empty;

    /// <summary>Creation timestamp, ISO 8601 round-trip format.</summary>
    public string BackupDate { get; init; } = string.Empty;

    /// <summary>Image format discriminator; always <c>RawImage</c>.</summary>
    public string Mode { get; init; } = "RawImage";

    /// <summary>Number of bytes stored in the image.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Streaming block size in bytes.</summary>
    public int BlockSize { get; init; }

    /// <summary>Bytes that could not be read and were replaced by zeros.</summary>
    public long UnreadableBytes { get; init; }

    /// <summary>SHA-256 of the complete image.</summary>
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>One progress sample of a streaming copy operation.</summary>
public sealed record TuiSafeTransferProgress(
    long BytesProcessed,
    long TotalBytes,
    double SpeedMbps,
    TimeSpan Elapsed,
    TimeSpan? Eta,
    long UnreadableBytes = 0)
{
    /// <summary>Completion of the operation in percent.</summary>
    public double Percent => TotalBytes <= 0 ? 0 : Math.Clamp(BytesProcessed * 100d / TotalBytes, 0, 100);
}

/// <summary>Result of a completed raw backup.</summary>
public sealed record TuiSafeBackupResult(
    string ImagePath,
    string ManifestPath,
    TuiSafeBackupManifest Manifest,
    bool Verified)
{
    /// <summary>Bytes stored in the image.</summary>
    public long TotalBytes => Manifest.TotalBytes;

    /// <summary>Bytes that could not be read and were zero filled.</summary>
    public long UnreadableBytes => Manifest.UnreadableBytes;

    /// <summary>SHA-256 recorded in the manifest.</summary>
    public string Sha256 => Manifest.Sha256;

    /// <summary>
    /// A rescue image that contains zero filled gaps is not a lossless backup, so
    /// the destructive phase must never run on top of it.
    /// </summary>
    public bool CanRunDestructivePhase => Manifest.UnreadableBytes == 0;
}

/// <summary>Result of a completed restore.</summary>
public sealed record TuiSafeRestoreResult(string ImagePath, string TargetPath, long BytesWritten);

/// <summary>A volume that can hold the raw backup image.</summary>
public sealed record TuiSafeBackupTarget(string Root, string Label, long FreeBytes, long TotalBytes)
{
    /// <summary>True when the target can hold an image of <paramref name="requiredBytes"/>.</summary>
    public bool HasRoomFor(long requiredBytes) => FreeBytes >= requiredBytes;
}

/// <summary>
/// Streaming engine of the "safe destructive test" screen: raw image backup,
/// SHA-256 verification, restore and restore verification.
/// <para>
/// Every operation works on plain paths (source, target, size), so the complete
/// backup / verify / restore round-trip can be exercised against temporary files
/// without touching a physical disk. The destructive test itself is not part of
/// this service; it stays behind <see cref="DiskChecker.Core.Interfaces.IDiskSanitizationService"/>.
/// </para>
/// </summary>
public sealed class TuiSafeWorkflowService
{
    /// <summary>Streaming block size: 1 MiB, identical to the desktop workflow.</summary>
    public const int BlockSize = 1024 * 1024;

    /// <summary>Abort when this many consecutive blocks cannot be read.</summary>
    public const int MaxConsecutiveUnreadableBlocks = 64;

    /// <summary>File name of the raw sector image.</summary>
    public const string ImageFileName = "disk_image.raw";

    /// <summary>File name of the backup manifest.</summary>
    public const string ManifestFileName = "backup_manifest.json";

    /// <summary>Prefix of the per-run backup folder.</summary>
    public const string BackupFolderPrefix = "DiskChecker_SafeBackup_";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Opens the source device read only. Replaced by tests to simulate unreadable
    /// blocks without a failing physical device.
    /// </summary>
    internal Func<string, Stream> SourceStreamFactory { get; set; } = CreateSourceStream;

    /// <summary>
    /// Opens the restore target for writing. Replaced by tests that do not want to
    /// write into a device path.
    /// </summary>
    internal Func<string, Stream> TargetStreamFactory { get; set; } = CreateTargetStream;

    /// <summary>
    /// Streams the complete source device into a raw image inside a fresh backup
    /// folder below <paramref name="targetDirectory"/> and writes the manifest.
    /// Unreadable blocks are zero filled and counted; the operation aborts when more
    /// than <see cref="MaxConsecutiveUnreadableBlocks"/> blocks in a row fail.
    /// </summary>
    public async Task<TuiSafeBackupResult> CreateBackupAsync(
        string sourcePath,
        string targetDirectory,
        long totalBytes,
        string sourceModel = "",
        IProgress<TuiSafeTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytes);

        string folder = CreateBackupFolder(targetDirectory);
        string imagePath = Path.Combine(folder, ImageFileName);
        string manifestPath = Path.Combine(folder, ManifestFileName);

        var buffer = new byte[BlockSize];
        long bytesCopied = 0;
        long unreadableBytes = 0;
        int consecutiveUnreadable = 0;
        var startedAt = Stopwatch.StartNew();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (Stream source = SourceStreamFactory(sourcePath))
        await using (var image = new FileStream(imagePath, FileMode.Create, FileAccess.Write, FileShare.Read, BlockSize, FileOptions.SequentialScan))
        {
            while (bytesCopied < totalBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int wanted = (int)Math.Min(BlockSize, totalBytes - bytesCopied);
                int read;
                try
                {
                    read = await source.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    read = 0;
                }

                if (read <= 0)
                {
                    // Unreadable sector: the image stays restorable up to this point,
                    // the gap is preserved as zeros and counted in the manifest.
                    Array.Clear(buffer, 0, wanted);
                    read = wanted;
                    unreadableBytes += wanted;
                    consecutiveUnreadable++;
                    if (consecutiveUnreadable >= MaxConsecutiveUnreadableBlocks)
                    {
                        throw new IOException(string.Create(
                            CultureInfo.InvariantCulture,
                            $"Zdroj nelze číst: {consecutiveUnreadable} nečitelných bloků za sebou na pozici {TuiFormats.Bytes(bytesCopied)}. Záloha přerušena."));
                    }
                }
                else
                {
                    consecutiveUnreadable = 0;
                }

                await image.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                bytesCopied += read;

                progress?.Report(BuildProgress(bytesCopied, totalBytes, unreadableBytes, startedAt.Elapsed));
            }

            await image.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var manifest = new TuiSafeBackupManifest
        {
            SourceDrive = sourcePath,
            SourceModel = sourceModel,
            BackupDate = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
            Mode = "RawImage",
            TotalBytes = bytesCopied,
            BlockSize = BlockSize,
            UnreadableBytes = unreadableBytes,
            Sha256 = Convert.ToHexString(hash.GetHashAndReset()),
        };

        await WriteManifestAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
        return new TuiSafeBackupResult(imagePath, manifestPath, manifest, Verified: false);
    }

    /// <summary>
    /// Recomputes the SHA-256 of the image and compares it with the manifest value.
    /// </summary>
    public async Task<bool> VerifyImageAsync(
        string imagePath,
        string expectedSha256,
        long totalBytes,
        IProgress<TuiSafeTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        if (!File.Exists(imagePath))
        {
            return false;
        }

        if (new FileInfo(imagePath).Length < totalBytes)
        {
            return false;
        }

        string actual = await ComputeRegionSha256Async(imagePath, totalBytes, progress, cancellationToken).ConfigureAwait(false);
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Streams the image back onto the target in 1 MiB blocks.
    /// </summary>
    public async Task<TuiSafeRestoreResult> RestoreAsync(
        string imagePath,
        string targetPath,
        long totalBytes,
        IProgress<TuiSafeTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytes);

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Záložní obraz nebyl nalezen.", imagePath);
        }

        var buffer = new byte[BlockSize];
        long bytesWritten = 0;
        var startedAt = Stopwatch.StartNew();

        await using (var image = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize, FileOptions.SequentialScan))
        await using (Stream target = TargetStreamFactory(targetPath))
        {
            while (bytesWritten < totalBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int wanted = (int)Math.Min(BlockSize, totalBytes - bytesWritten);
                int read = await image.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new EndOfStreamException(
                        $"Obraz je kratší než manifest: obnoveno {TuiFormats.Bytes(bytesWritten)} z {TuiFormats.Bytes(totalBytes)}.");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                bytesWritten += read;
                progress?.Report(BuildProgress(bytesWritten, totalBytes, 0, startedAt.Elapsed));
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return new TuiSafeRestoreResult(imagePath, targetPath, bytesWritten);
    }

    /// <summary>
    /// Verifies the restored region on the target by recomputing its SHA-256.
    /// </summary>
    public async Task<bool> VerifyRestoreAsync(
        string targetPath,
        long totalBytes,
        string expectedSha256,
        IProgress<TuiSafeTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        if (!File.Exists(targetPath))
        {
            return false;
        }

        string actual = await ComputeRegionSha256Async(targetPath, totalBytes, progress, cancellationToken).ConfigureAwait(false);
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Computes the SHA-256 of the first <paramref name="totalBytes"/> bytes of a file.
    /// </summary>
    public async Task<string> ComputeRegionSha256Async(
        string path,
        long totalBytes,
        IProgress<TuiSafeTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytes);

        var buffer = new byte[BlockSize];
        long hashed = 0;
        var startedAt = Stopwatch.StartNew();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BlockSize, FileOptions.SequentialScan);
        while (hashed < totalBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int wanted = (int)Math.Min(BlockSize, totalBytes - hashed);
            int read = await stream.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new EndOfStreamException(
                    $"Neočekávaný konec souboru při výpočtu kontrolního součtu ({TuiFormats.Bytes(hashed)} z {TuiFormats.Bytes(totalBytes)}).");
            }

            hash.AppendData(buffer, 0, read);
            hashed += read;
            progress?.Report(BuildProgress(hashed, totalBytes, 0, startedAt.Elapsed));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    /// Creates the per-run backup folder <c>DiskChecker_SafeBackup_yyyyMMdd_HHmmss</c>.
    /// </summary>
    public static string CreateBackupFolder(string targetDirectory, DateTime? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        Directory.CreateDirectory(targetDirectory);
        string baseName = BackupFolderPrefix +
                          (timestamp ?? DateTime.Now).ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string candidate = Path.Combine(targetDirectory, baseName);
        int suffix = 1;
        while (Directory.Exists(candidate))
        {
            suffix++;
            candidate = Path.Combine(targetDirectory, baseName + "_" + suffix.ToString(CultureInfo.InvariantCulture));
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    /// <summary>Serializes a manifest for <c>backup_manifest.json</c>.</summary>
    public static string SerializeManifest(TuiSafeBackupManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, ManifestJsonOptions);
    }

    /// <summary>Parses the contents of <c>backup_manifest.json</c>.</summary>
    public static TuiSafeBackupManifest? DeserializeManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TuiSafeBackupManifest>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes <c>backup_manifest.json</c> next to the image.</summary>
    public static async Task WriteManifestAsync(
        string manifestPath,
        TuiSafeBackupManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(manifest);

        await File.WriteAllTextAsync(manifestPath, SerializeManifest(manifest), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads <c>backup_manifest.json</c>, returning <c>null</c> when it is unusable.</summary>
    public static async Task<TuiSafeBackupManifest?> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        if (!File.Exists(manifestPath))
        {
            return null;
        }

        string json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        return DeserializeManifest(json);
    }

    /// <summary>Discovers volumes that could hold the backup image.</summary>
    public static IReadOnlyList<TuiSafeBackupTarget> DiscoverTargets(string? excludedPath = null)
    {
        var targets = new List<TuiSafeBackupTarget>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    long free = drive.AvailableFreeSpace;
                    if (free <= 0)
                    {
                        continue;
                    }

                    string root = drive.RootDirectory.FullName;
                    if (!string.IsNullOrEmpty(excludedPath) && string.Equals(root, excludedPath, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    targets.Add(new TuiSafeBackupTarget(
                        root,
                        $"{root} ({drive.DriveFormat})",
                        free,
                        drive.TotalSize));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // A volume that vanished between enumeration and inspection is skipped.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Disk enumeration is best effort; the caller reports an empty target list.
        }

        return targets;
    }

    private static TuiSafeTransferProgress BuildProgress(long processed, long total, long unreadableBytes, TimeSpan elapsed)
    {
        double seconds = elapsed.TotalSeconds;
        double bytesPerSecond = seconds > 0.5 ? processed / seconds : 0;
        double speedMbps = bytesPerSecond / (1024d * 1024d);
        TimeSpan? eta = bytesPerSecond > 0 && total > processed
            ? TimeSpan.FromSeconds((total - processed) / bytesPerSecond)
            : null;

        return new TuiSafeTransferProgress(processed, total, speedMbps, elapsed, eta, unreadableBytes);
    }

    private static Stream CreateSourceStream(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize, FileOptions.SequentialScan);

    private static Stream CreateTargetStream(string path) =>
        new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, BlockSize, FileOptions.SequentialScan);
}
