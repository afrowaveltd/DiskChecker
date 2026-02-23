namespace DiskChecker.Core.Models;

/// <summary>
/// Represents a drive technology category used for testing profiles.
/// </summary>
public enum DriveTechnology
{
    /// <summary>
    /// Unknown or not detected technology.
    /// </summary>
    Unknown,
    /// <summary>
    /// Traditional spinning HDD.
    /// </summary>
    Hdd,
    /// <summary>
    /// SATA/SAS SSD.
    /// </summary>
    Ssd,
    /// <summary>
    /// NVMe SSD.
    /// </summary>
    Nvme
}

/// <summary>
/// Defines a surface test profile.
/// </summary>
public enum SurfaceTestProfile
{
    /// <summary>
    /// Full write/read surface test for HDD.
    /// </summary>
    HddFull,
    /// <summary>
    /// Quick, SSD-friendly test profile.
    /// </summary>
    SsdQuick,
    /// <summary>
    /// Custom profile configured by the user.
    /// </summary>
    Custom
}

/// <summary>
/// Defines the type of surface test operation.
/// </summary>
public enum SurfaceTestOperation
{
    /// <summary>
    /// Read-only scan of the device surface.
    /// </summary>
    ReadOnly,
    /// <summary>
    /// Write a zero pattern and verify by reading.
    /// </summary>
    WriteZeroFill,
    /// <summary>
    /// Write a data pattern and verify by reading.
    /// </summary>
    WritePattern
}

/// <summary>
/// Describes a surface test request.
/// </summary>
public class SurfaceTestRequest
{
    /// <summary>
    /// Gets or sets the drive information.
    /// </summary>
    public CoreDriveInfo Drive { get; set; } = new();

    /// <summary>
    /// Gets or sets the detected drive technology.
    /// </summary>
    public DriveTechnology Technology { get; set; } = DriveTechnology.Unknown;

    /// <summary>
    /// Gets or sets the test profile.
    /// </summary>
    public SurfaceTestProfile Profile { get; set; } = SurfaceTestProfile.HddFull;

    /// <summary>
    /// Gets or sets the surface test operation.
    /// </summary>
    public SurfaceTestOperation Operation { get; set; } = SurfaceTestOperation.WriteZeroFill;

    /// <summary>
    /// Gets or sets the block size used for IO operations in bytes.
    /// </summary>
    public int BlockSizeBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets how many blocks are processed between speed samples.
    /// </summary>
    public int SampleIntervalBlocks { get; set; } = 128;

    /// <summary>
    /// Gets or sets the maximum number of bytes to test.
    /// </summary>
    public long? MaxBytesToTest { get; set; }

    /// <summary>
    /// Gets or sets whether secure erase should be attempted.
    /// </summary>
    public bool SecureErase { get; set; }

    /// <summary>
    /// Gets or sets whether write access to device paths is allowed.
    /// </summary>
    public bool AllowDeviceWrite { get; set; }

    /// <summary>
    /// Gets or sets the requester identity for audit logs.
    /// </summary>
    public string? RequestedBy { get; set; }
}

/// <summary>
/// Represents a single throughput sample for the surface test.
/// </summary>
public class SurfaceTestSample
{
    /// <summary>
    /// Gets or sets the byte offset where the sample was recorded.
    /// </summary>
    public long OffsetBytes { get; set; }

    /// <summary>
    /// Gets or sets the size of the sample window in bytes.
    /// </summary>
    public int BlockSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the measured throughput in MB/s.
    /// </summary>
    public double ThroughputMbps { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the sample.
    /// </summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of errors recorded in the sample window.
    /// </summary>
    public int ErrorCount { get; set; }
}

/// <summary>
/// Reports progress during the surface test.
/// </summary>
public class SurfaceTestProgress
{
    /// <summary>
    /// Gets or sets the test identifier.
    /// </summary>
    public Guid TestId { get; set; }

    /// <summary>
    /// Gets or sets the percent complete (0-100).
    /// </summary>
    public double PercentComplete { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes processed so far.
    /// </summary>
    public long BytesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the current throughput in MB/s.
    /// </summary>
    public double CurrentThroughputMbps { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the progress update.
    /// </summary>
    public DateTime TimestampUtc { get; set; }
}

/// <summary>
/// Represents the outcome of a surface test.
/// </summary>
public class SurfaceTestResult
{
    /// <summary>
    /// Gets or sets the test identifier.
    /// </summary>
    public Guid TestId { get; set; }

    /// <summary>
    /// Gets or sets the drive that was tested.
    /// </summary>
    public CoreDriveInfo Drive { get; set; } = new();

    /// <summary>
    /// Gets or sets the drive technology.
    /// </summary>
    public DriveTechnology Technology { get; set; } = DriveTechnology.Unknown;

    /// <summary>
    /// Gets or sets the profile used for the test.
    /// </summary>
    public SurfaceTestProfile Profile { get; set; }

    /// <summary>
    /// Gets or sets the operation type.
    /// </summary>
    public SurfaceTestOperation Operation { get; set; }

    /// <summary>
    /// Gets or sets the start time in UTC.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the completion time in UTC.
    /// </summary>
    public DateTime CompletedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the total bytes tested.
    /// </summary>
    public long TotalBytesTested { get; set; }

    /// <summary>
    /// Gets or sets the average throughput in MB/s.
    /// </summary>
    public double AverageSpeedMbps { get; set; }

    /// <summary>
    /// Gets or sets the peak throughput in MB/s.
    /// </summary>
    public double PeakSpeedMbps { get; set; }

    /// <summary>
    /// Gets or sets the minimum throughput in MB/s.
    /// </summary>
    public double MinSpeedMbps { get; set; }

    /// <summary>
    /// Gets or sets the total error count.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Gets or sets whether secure erase was performed.
    /// </summary>
    public bool SecureErasePerformed { get; set; }

    /// <summary>
    /// Gets or sets the speed samples captured during the test.
    /// </summary>
    public IReadOnlyList<SurfaceTestSample> Samples { get; set; } = new List<SurfaceTestSample>();

    /// <summary>
    /// Gets or sets optional notes about the run.
    /// </summary>
    public string? Notes { get; set; }
}
