
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
   /// Complete disk sanitization - full disk erase and validation for storage/archival.
   /// </summary>
   FullDiskSanitization,
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
/// Comprehensive test result with drive information and metrics.
/// </summary>
public class SurfaceTestResult
{
   /// <summary>
   /// Unique test identifier.
   /// </summary>
   public string TestId { get; set; } = Guid.NewGuid().ToString();

   /// <summary>
   /// Test timestamp (UTC).
   /// </summary>
   public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

   /// <summary>
   /// When test completed.
   /// </summary>
   public DateTime CompletedAtUtc { get; set; }

   // === Drive Information ===

   /// <summary>
   /// Drive model/name (e.g., "ST500DM002").
   /// </summary>
   public string? DriveModel { get; set; }

   /// <summary>
   /// Drive serial number.
   /// </summary>
   public string? DriveSerialNumber { get; set; }

   /// <summary>
   /// Drive manufacturer (e.g., "Seagate", "Samsung").
   /// </summary>
   public string? DriveManufacturer { get; set; }

   /// <summary>
   /// Drive interface type (SATA, NVMe, SAS, USB).
   /// </summary>
   public string? DriveInterface { get; set; }

   /// <summary>
   /// Exact drive capacity in bytes.
   /// </summary>
   public long DriveTotalBytes { get; set; }

   /// <summary>
   /// Drive speed (RPM for HDD, N/A for SSD).
   /// </summary>
   public int? DriveRpmOrNvmeSpeed { get; set; }

   /// <summary>
   /// Power-on hours (from SMART).
   /// </summary>
   public long? PowerOnHours { get; set; }

   /// <summary>
   /// Current temperature in Celsius (from SMART).
   /// </summary>
   public int? CurrentTemperatureCelsius { get; set; }

   /// <summary>
   /// Reallocated sectors count (from SMART) - indicator of wear.
   /// </summary>
   public long? ReallocatedSectors { get; set; }

   // === Test Configuration ===

   /// <summary>
   /// Test profile (HDD, SSD, FullDiskSanitization).
   /// </summary>
   public SurfaceTestProfile Profile { get; set; }

   /// <summary>
   /// Operation performed (ReadOnly, WriteZeroFill, etc.).
   /// </summary>
   public SurfaceTestOperation Operation { get; set; }

   // === Test Metrics ===

   /// <summary>
   /// Total bytes tested (written + read).
   /// </summary>
   public long TotalBytesTested { get; set; }

   /// <summary>
   /// Average throughput in MB/s.
   /// </summary>
   public double AverageSpeedMbps { get; set; }

   /// <summary>
   /// Peak throughput in MB/s.
   /// </summary>
   public double PeakSpeedMbps { get; set; }

   /// <summary>
   /// Minimum throughput in MB/s.
   /// </summary>
   public double MinSpeedMbps { get; set; }

   /// <summary>
   /// Number of errors detected.
   /// </summary>
   public int ErrorCount { get; set; }

   /// <summary>
   /// Whether disk was securely erased.
   /// </summary>
   public bool SecureErasePerformed { get; set; }

   /// <summary>
   /// Sample data points during test.
   /// </summary>
   public List<SurfaceTestSample> Samples { get; set; } = new();

   /// <summary>
   /// Human-readable notes/summary.
   /// </summary>
   public string? Notes { get; set; }
}
