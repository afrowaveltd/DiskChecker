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

    /// <summary>
    /// Create a GPT partition on the device and optionally format it.
    /// Does NOT write zeros or verify — only creates partition structure.
    /// </summary>
    /// <param name="devicePath">Device path (e.g., /dev/sda on Linux, \\.\PhysicalDrive0 on Windows)</param>
    /// <param name="volumeLabel">Label for the new volume</param>
    /// <param name="format">Whether to format the partition (NTFS on Windows, ext4 on Linux)</param>
    /// <param name="progress">Progress reporter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with partition creation status</returns>
    Task<SanitizationResult> CreatePartitionAsync(
        string devicePath,
        string volumeLabel = "Tested",
        bool format = true,
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

    /// <summary>
    /// Time spent in the zero-fill write phase, excluding partitioning/formatting.
    /// </summary>
    public TimeSpan WriteDuration { get; set; }

    /// <summary>
    /// Time spent in the read/verify phase, excluding partitioning/formatting.
    /// </summary>
    public TimeSpan ReadDuration { get; set; }

    public bool PartitionCreated { get; set; }
    public bool Formatted { get; set; }

    /// <summary>
    /// SMART snapshot captured after sanitization when available.
    /// </summary>
    public DiskChecker.Core.Models.SmartaData? SmartAfter { get; set; }

    /// <summary>
    /// File system that was created (e.g., "ext4" on Linux, "NTFS" on Windows).
    /// </summary>
    public string? FileSystem { get; set; }

    /// <summary>
    /// Volume label that was set on the formatted partition.
    /// </summary>
    public string? VolumeLabel { get; set; }

    /// <summary>
    /// Detailed error entries collected during sanitization.
    /// </summary>
    public List<SanitizationErrorDetail> ErrorDetails { get; set; } = new();
}

/// <summary>
/// Detailed information about a sanitization error.
/// </summary>
public class SanitizationErrorDetail
{
    /// <summary>
    /// Gets or sets the phase where the error occurred.
    /// </summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets short error code.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional detailed diagnostic text.
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Gets or sets approximate byte offset related to the error.
    /// </summary>
    public long? OffsetBytes { get; set; }
}
/// <summary>
/// Progress callback for sanitization operations.
/// </summary>
public class SanitizationProgress
{
    /// <summary>
    /// Stable machine-readable phase used by the UI. Phase remains the localized display text.
    /// </summary>
    public SanitizationProgressPhase PhaseKind { get; set; }

    public string Phase { get; set; } = "";
    public double ProgressPercent { get; set; }
    public long BytesProcessed { get; set; }
    public long TotalBytes { get; set; }
    public double CurrentSpeedMBps { get; set; }
    public int Errors { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Elapsed time from the beginning of the whole sanitization/test run.
    /// </summary>
    public TimeSpan? TotalElapsed { get; set; }

    /// <summary>
    /// Elapsed time from the beginning of the current write/read phase.
    /// </summary>
    public TimeSpan? PhaseElapsed { get; set; }

    /// <summary>
    /// True while the current write/read request is still waiting for the device after the stall threshold.
    /// BytesProcessed and ProgressPercent intentionally remain unchanged for these samples.
    /// </summary>
    public bool IsStalled { get; set; }

    /// <summary>
    /// Alias for UI text that wants to show the user that the application is waiting on the drive.
    /// </summary>
    public bool IsWaitingForDevice { get; set; }

    /// <summary>
    /// Elapsed time of the currently outstanding write/read operation.
    /// </summary>
    public TimeSpan? CurrentOperationElapsed { get; set; }

    /// <summary>
    /// Portion of the current operation elapsed time beyond the configured stall threshold.
    /// </summary>
    public TimeSpan? StallDuration { get; set; }

    /// <summary>
    /// Speed of the completed I/O chunk only. This excludes idle time between chunks but includes time spent inside that I/O call.
    /// </summary>
    public double? RawOperationSpeedMBps { get; set; }

    /// <summary>
    /// Phase-level throughput including all stalls observed so far.
    /// </summary>
    public double? EffectiveSpeedMBps { get; set; }

    /// <summary>
    /// Optional additional status detail (e.g., recovery attempt reason).
    /// </summary>
    public string? StatusDetail { get; set; }

    public bool IsWritePhase =>
        PhaseKind == SanitizationProgressPhase.Write ||
        Phase.StartsWith("Write zeros", StringComparison.Ordinal);

    public bool IsReadVerifyPhase =>
        PhaseKind == SanitizationProgressPhase.ReadVerify ||
        Phase.StartsWith("Read and verify", StringComparison.Ordinal);
}

public enum SanitizationProgressPhase
{
    Other,
    Write,
    ReadVerify
}
