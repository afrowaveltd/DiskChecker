namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Platform-independent interface for disk sanitization operations.
/// WARNING: Sanitization operations are DESTRUCTIVE and will ERASE ALL DATA!
/// </summary>
public interface IDiskSanitizationService
{
    /// <summary>
    /// Perform full disk sanitization: write zeros, read/verify, optionally create partition and format.
    /// WARNING: This destroys ALL data on the disk!
    /// </summary>
    /// <param name="devicePath">Device path (e.g., /dev/sda on Linux, \\.\PhysicalDrive0 on Windows)</param>
    /// <param name="diskSize">Total size of the disk in bytes</param>
    /// <param name="createPartition">Whether to create a partition after sanitization</param>
    /// <param name="format">Whether to format the partition (NTFS on Windows, ext4 on Linux)</param>
    /// <param name="volumeLabel">Label for the new volume</param>
    /// <param name="progress">Progress reporter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the sanitization operation</returns>
    Task<SanitizationResult> SanitizeDiskAsync(
        string devicePath,
        long diskSize,
        bool createPartition = true,
        bool format = true,
        string volumeLabel = "Sanitized",
        IProgress<SanitizationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of disk sanitization operation.
/// </summary>
public class SanitizationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long BytesWritten { get; set; }
    public long BytesRead { get; set; }
    public double WriteSpeedMBps { get; set; }
    public double ReadSpeedMBps { get; set; }
    public int ErrorsDetected { get; set; }
    public TimeSpan Duration { get; set; }
    public bool PartitionCreated { get; set; }
    public bool Formatted { get; set; }
}

/// <summary>
/// Progress callback for sanitization operations.
/// </summary>
public class SanitizationProgress
{
    public string Phase { get; set; } = "";
    public double ProgressPercent { get; set; }
    public long BytesProcessed { get; set; }
    public long TotalBytes { get; set; }
    public double CurrentSpeedMBps { get; set; }
    public int Errors { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
}