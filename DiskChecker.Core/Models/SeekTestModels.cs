using System;
using System.Collections.Generic;

namespace DiskChecker.Core.Models;

/// <summary>
/// Types of seek tests supported by the application.
/// </summary>
public enum SeekTestType
{
    /// <summary>Full-stroke seek: sweeps the entire LBA range from min to max and back.</summary>
    FullStroke = 0,

    /// <summary>Random seek: jumps to random positions across the full LBA range.</summary>
    Random = 1,

    /// <summary>Skip seek: jumps by a fixed number of segments (e.g., 1000 segments).</summary>
    Skip = 2
}

/// <summary>
/// SMART-informed recommendation for seek test parameters.
/// The recommendation engine respects disk age (PowerOnHours) and wear indicators
/// to avoid torturing old or fragile drives with excessive seek counts.
/// </summary>
public class SeekTestRecommendation
{
    /// <summary>
    /// Recommended seek test type.
    /// </summary>
    public SeekTestType RecommendedType { get; set; } = SeekTestType.FullStroke;

    /// <summary>
    /// Recommended number of seek operations.
    /// </summary>
    public int RecommendedSeekCount { get; set; } = 1000;

    /// <summary>
    /// Recommended segment count for skip seeks (how many LBA segments to jump).
    /// </summary>
    public int RecommendedSkipSegments { get; set; } = 1000;

    /// <summary>
    /// Human-readable explanation of why this recommendation was made.
    /// </summary>
    public string Rationale { get; set; } = string.Empty;

    /// <summary>
    /// Whether the recommendation is conservative (reduced seek count due to disk age/wear).
    /// </summary>
    public bool IsConservative { get; set; }

    /// <summary>
    /// Whether the disk is considered too fragile for seek testing at all.
    /// </summary>
    public bool IsTooFragile { get; set; }

    /// <summary>
    /// Maximum safe seek count for this disk (hard ceiling).
    /// </summary>
    public int MaxSafeSeekCount { get; set; } = 5000;

    /// <summary>
    /// Disk power-on hours used for the recommendation.
    /// </summary>
    public int? PowerOnHours { get; set; }

    /// <summary>
    /// Disk reallocated sector count used for the recommendation.
    /// </summary>
    public int? ReallocatedSectors { get; set; }

    /// <summary>
    /// Whether the disk is an SSD (seek tests are less meaningful for SSDs).
    /// </summary>
    public bool IsSolidState { get; set; }
}

/// <summary>
/// Describes a seek test request with all configuration parameters.
/// </summary>
public class SeekTestRequest
{
    /// <summary>
    /// Drive information for the disk to test.
    /// </summary>
    public CoreDriveInfo Drive { get; set; } = new();

    /// <summary>
    /// Type of seek test to perform.
    /// </summary>
    public SeekTestType TestType { get; set; } = SeekTestType.FullStroke;

    /// <summary>
    /// Number of seek operations to perform.
    /// </summary>
    public int SeekCount { get; set; } = 1000;

    /// <summary>
    /// For skip seeks: number of LBA segments to jump per seek.
    /// </summary>
    public int SkipSegments { get; set; } = 1000;

    /// <summary>
    /// Block size for each read operation at the seek destination (bytes).
    /// Default 4096 (one sector read to confirm position).
    /// </summary>
    public int BlockSizeBytes { get; set; } = 4096;

    /// <summary>
    /// Whether to collect latency samples for each seek.
    /// </summary>
    public bool CollectLatencySamples { get; set; } = true;

    /// <summary>
    /// Maximum time allowed for the test (seconds). 0 = no limit.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Requester identity for audit logs.
    /// </summary>
    public string? RequestedBy { get; set; }
}

/// <summary>
/// Represents a single seek latency sample.
/// </summary>
public class SeekLatencySample
{
    /// <summary>
    /// Sample index (1-based).
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Source LBA position.
    /// </summary>
    public long SourceLba { get; set; }

    /// <summary>
    /// Destination LBA position.
    /// </summary>
    public long DestinationLba { get; set; }

    /// <summary>
    /// Seek distance in LBA sectors.
    /// </summary>
    public long SeekDistance { get; set; }

    /// <summary>
    /// Measured seek latency in milliseconds.
    /// </summary>
    public double LatencyMs { get; set; }

    /// <summary>
    /// Timestamp when the sample was recorded.
    /// </summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// Whether an error occurred during this seek.
    /// </summary>
    public bool HasError { get; set; }

    /// <summary>
    /// Error message if any.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Reports progress during a seek test.
/// </summary>
public class SeekTestProgress
{
    /// <summary>
    /// Test identifier.
    /// </summary>
    public Guid TestId { get; set; }

    /// <summary>
    /// Percent complete (0-100).
    /// </summary>
    public double PercentComplete { get; set; }

    /// <summary>
    /// Number of seeks completed so far.
    /// </summary>
    public int SeeksCompleted { get; set; }

    /// <summary>
    /// Total seeks planned.
    /// </summary>
    public int TotalSeeks { get; set; }

    /// <summary>
    /// Current average latency in milliseconds.
    /// </summary>
    public double CurrentAverageLatencyMs { get; set; }

    /// <summary>
    /// Timestamp of the progress update.
    /// </summary>
    public DateTime TimestampUtc { get; set; }
}

/// <summary>
/// Comprehensive seek test result.
/// </summary>
public class SeekTestResult
{
    /// <summary>
    /// Unique test identifier.
    /// </summary>
    public string TestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the test started (UTC).
    /// </summary>
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the test completed (UTC).
    /// </summary>
    public DateTime CompletedAtUtc { get; set; }

    // === Drive Information ===

    /// <summary>
    /// Drive model/name.
    /// </summary>
    public string? DriveModel { get; set; }

    /// <summary>
    /// Drive serial number.
    /// </summary>
    public string? DriveSerialNumber { get; set; }

    /// <summary>
    /// Drive path (e.g., /dev/sda or \\.\PhysicalDrive0).
    /// </summary>
    public string? DrivePath { get; set; }

    /// <summary>
    /// Total drive capacity in bytes.
    /// </summary>
    public long DriveTotalBytes { get; set; }

    /// <summary>
    /// Power-on hours from SMART.
    /// </summary>
    public long? PowerOnHours { get; set; }

    // === Test Configuration ===

    /// <summary>
    /// Type of seek test performed.
    /// </summary>
    public SeekTestType TestType { get; set; }

    /// <summary>
    /// Number of seeks performed.
    /// </summary>
    public int SeekCount { get; set; }

    /// <summary>
    /// Skip segments used (for skip seeks).
    /// </summary>
    public int SkipSegments { get; set; }

    // === Test Metrics ===

    /// <summary>
    /// Average seek latency in milliseconds.
    /// </summary>
    public double AverageLatencyMs { get; set; }

    /// <summary>
    /// Minimum seek latency in milliseconds.
    /// </summary>
    public double MinLatencyMs { get; set; }

    /// <summary>
    /// Maximum seek latency in milliseconds.
    /// </summary>
    public double MaxLatencyMs { get; set; }

    /// <summary>
    /// Standard deviation of seek latency.
    /// </summary>
    public double LatencyStdDevMs { get; set; }

    /// <summary>
    /// Number of seek errors encountered.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Total test duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Whether the test completed successfully.
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Whether the test was aborted (timeout, cancellation, etc.).
    /// </summary>
    public bool WasAborted { get; set; }

    /// <summary>
    /// Latency samples collected during the test.
    /// </summary>
    public List<SeekLatencySample> Samples { get; set; } = new();

    /// <summary>
    /// Human-readable notes/summary.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// The SMART-informed recommendation that was used.
    /// </summary>
    public SeekTestRecommendation? Recommendation { get; set; }
}
