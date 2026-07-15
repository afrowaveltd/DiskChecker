using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace DiskChecker.Core.Models;

/// <summary>
/// Certificate generated after successful disk testing.
/// Can be printed as PDF for documentation.
/// </summary>
public class DiskCertificate
{
    public int Id { get; set; }
    
    /// <summary>
    /// Unique certificate number
    /// </summary>
    public string CertificateNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Foreign key to DiskCard
    /// </summary>
    public int DiskCardId { get; set; }
    
    /// <summary>
    /// Navigation property
    /// </summary>
    public DiskCard? DiskCard { get; set; }
    
    /// <summary>
    /// Foreign key to TestSession
    /// </summary>
    public int TestSessionId { get; set; }
    
    /// <summary>
    /// Navigation property
    /// </summary>
    public TestSession? TestSession { get; set; }
    
    /// <summary>
    /// When certificate was generated
    /// </summary>
    public DateTime GeneratedAt { get; set; }
    
    /// <summary>
    /// Who generated the certificate (user/operator)
    /// </summary>
    public string GeneratedBy { get; set; } = string.Empty;
    
    // ========== Disk Information ==========
    
    /// <summary>
    /// Disk model at time of certification
    /// </summary>
    public string DiskModel { get; set; } = string.Empty;
    
    /// <summary>
    /// Disk serial number
    /// </summary>
    public string SerialNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Disk capacity formatted (e.g., "500 GB")
    /// </summary>
    public string Capacity { get; set; } = string.Empty;
    
    /// <summary>
    /// Disk type (SSD, HDD, NVMe)
    /// </summary>
    public string DiskType { get; set; } = string.Empty;
    
    /// <summary>
    /// Firmware version
    /// </summary>
    public string Firmware { get; set; } = string.Empty;
    
    /// <summary>
    /// Interface type
    /// </summary>
    public string Interface { get; set; } = string.Empty;
    
    // ========== Test Results ==========
    
    /// <summary>
    /// Test type performed
    /// </summary>
    public string TestType { get; set; } = string.Empty;
    
    /// <summary>
    /// Overall grade (A-F)
    /// </summary>
    public string Grade { get; set; } = string.Empty;
    
    /// <summary>
    /// Overall score (0-100)
    /// </summary>
    public double Score { get; set; }
    
    /// <summary>
    /// Health assessment
    /// </summary>
    public string HealthStatus { get; set; } = string.Empty;
    
    /// <summary>
    /// Test duration
    /// </summary>
    public TimeSpan TestDuration { get; set; }
    
    // ========== Performance Metrics ==========
    
    /// <summary>
    /// Average write speed
    /// </summary>
    public double AvgWriteSpeed { get; set; }
    
    /// <summary>
    /// Maximum write speed
    /// </summary>
    public double MaxWriteSpeed { get; set; }
    
    /// <summary>
    /// Average read speed
    /// </summary>
    public double AvgReadSpeed { get; set; }
    
    /// <summary>
    /// Maximum read speed
    /// </summary>
    public double MaxReadSpeed { get; set; }
    
    /// <summary>
    /// Temperature range during test
    /// </summary>
    public string TemperatureRange { get; set; } = string.Empty;
    
    /// <summary>
    /// Errors encountered
    /// </summary>
    public int ErrorCount { get; set; }
    
    // ========== SMART Summary ==========
    
    /// <summary>
    /// SMART health status
    /// </summary>
    public bool SmartPassed { get; set; }
    
    /// <summary>
    /// Power on hours
    /// </summary>
    public int PowerOnHours { get; set; }
    
    /// <summary>
    /// Power cycle count
    /// </summary>
    public int PowerCycles { get; set; }
    
    /// <summary>
    /// Reallocated sectors
    /// </summary>
    public int ReallocatedSectors { get; set; }
    
    /// <summary>
    /// Pending sectors
    /// </summary>
    public int PendingSectors { get; set; }
    
    /// <summary>
    /// Critical SMART attributes
    /// </summary>
    public List<SmartAttributeSummary> SmartAttributes { get; set; } = new();
    
    // ========== Sanitization Details ==========
    
    /// <summary>
    /// Whether sanitization was performed
    /// </summary>
    public bool SanitizationPerformed { get; set; }
    
    /// <summary>
    /// Sanitization method
    /// </summary>
    public string? SanitizationMethod { get; set; }
    
    /// <summary>
    /// Whether data was verified as zeroed
    /// </summary>
    public bool DataVerified { get; set; }
    
    /// <summary>
    /// Partition scheme
    /// </summary>
    public string? PartitionScheme { get; set; }
    
    /// <summary>
    /// File system
    /// </summary>
    public string? FileSystem { get; set; }
    
    /// <summary>
    /// Volume label
    /// </summary>
    public string? VolumeLabel { get; set; }
    
    // ========== Certificate Status ==========
    
    /// <summary>
    /// Certificate status
    /// </summary>
    public CertificateStatus Status { get; set; }
    
    /// <summary>
    /// Valid until date (for warranties)
    /// </summary>
    public DateTime? ValidUntil { get; set; }
    
    /// <summary>
    /// Whether PDF was generated
    /// </summary>
    public bool PdfGenerated { get; set; }
    
    /// <summary>
    /// Path to PDF file
    /// </summary>
    public string? PdfPath { get; set; }
    
    /// <summary>
    /// Absolute path to a cached chart image used for certificate rendering.
    /// </summary>
    [NotMapped]
    public string? ChartImagePath { get; set; }

    /// <summary>
    /// Downsampled write speed points for certificate chart rendering (runtime only, not persisted).
    /// </summary>
    [NotMapped]
    public List<double> WriteProfilePoints { get; set; } = new();

    /// <summary>
    /// Downsampled read speed points for certificate chart rendering (runtime only, not persisted).
    /// </summary>
    [NotMapped]
    public List<double> ReadProfilePoints { get; set; } = new();

    /// <summary>
    /// Additional notes
    /// </summary>
    public string? Notes { get; set; }
    
    // ========== Seek Test Metrics (Absolute Destructive Test) ==========

    /// <summary>
    /// Average seek latency across all three seek test types (ms).
    /// </summary>
    public double? SeekAvgLatencyMs { get; set; }

    /// <summary>
    /// Minimum seek latency recorded (ms).
    /// </summary>
    public double? SeekMinLatencyMs { get; set; }

    /// <summary>
    /// Maximum seek latency recorded (ms).
    /// </summary>
    public double? SeekMaxLatencyMs { get; set; }

    /// <summary>
    /// Standard deviation of seek latencies (ms).
    /// </summary>
    public double? SeekStdDevLatencyMs { get; set; }

    /// <summary>
    /// 95th percentile seek latency (ms).
    /// </summary>
    public double? SeekP95LatencyMs { get; set; }

    /// <summary>
    /// Median seek latency (ms).
    /// </summary>
    [NotMapped]
    public double? SeekMedianLatencyMs { get; set; }

    /// <summary>
    /// 99th percentile seek latency (ms).
    /// </summary>
    [NotMapped]
    public double? SeekP99LatencyMs { get; set; }

    /// <summary>
    /// Total number of seek operations represented by the certificate.
    /// </summary>
    [NotMapped]
    public int? SeekTotalCount { get; set; }

    /// <summary>
    /// Number of seek errors represented by the certificate.
    /// </summary>
    [NotMapped]
    public int? SeekErrorCount { get; set; }

    /// <summary>
    /// Downsampled seek latency points for certificate chart rendering.
    /// </summary>
    [NotMapped]
    public List<double> SeekLatencyPoints { get; set; } = new();

    /// <summary>
    /// Per-seek-test-type summaries for full/absolute certificates.
    /// </summary>
    [NotMapped]
    public List<string> SeekTypeSummaries { get; set; } = new();

    /// <summary>
    /// Human-readable summary of seek test results per type.
    /// </summary>
    public string? SeekTestSummary { get; set; }

    /// <summary>
    /// Downsampled temperature points for certificate chart rendering.
    /// </summary>
    [NotMapped]
    public List<double> TemperatureProfilePoints { get; set; } = new();

    /// <summary>
    /// Downsampled stall flags (true = device was unresponsive at this point).
    /// Same length as WriteProfilePoints/ReadProfilePoints.
    /// </summary>
    [NotMapped]
    public List<bool> StallProfilePoints { get; set; } = new();

    // ========== Absolute Destructive - Sanitization Pass Details (4 series) ==========

    /// <summary>
    /// Speed profile points for first sanitization pass - write phase.
    /// </summary>
    [NotMapped]
    public List<double> Sanitize1WritePoints { get; set; } = new();

    /// <summary>
    /// Speed profile points for first sanitization pass - read/verify phase.
    /// </summary>
    [NotMapped]
    public List<double> Sanitize1ReadPoints { get; set; } = new();

    /// <summary>
    /// Speed profile points for second sanitization pass - write phase.
    /// </summary>
    [NotMapped]
    public List<double> Sanitize2WritePoints { get; set; } = new();

    /// <summary>
    /// Speed profile points for second sanitization pass - read/verify phase.
    /// </summary>
    [NotMapped]
    public List<double> Sanitize2ReadPoints { get; set; } = new();
    // ========== Before/After Sanitization Comparison ==========

    /// <summary>
    /// Average write speed from first sanitization (MB/s).
    /// </summary>
    public double? Sanitize1AvgWriteMBps { get; set; }

    /// <summary>
    /// Average write speed from second sanitization (MB/s).
    /// </summary>
    public double? Sanitize2AvgWriteMBps { get; set; }

    /// <summary>
    /// Percentage change in write speed between sanitizations.
    /// </summary>
    public double? WriteSpeedChangePercent { get; set; }

    /// <summary>
    /// Average read speed from first sanitization (MB/s).
    /// </summary>
    public double? Sanitize1AvgReadMBps { get; set; }

    /// <summary>
    /// Average read speed from second sanitization (MB/s).
    /// </summary>
    public double? Sanitize2AvgReadMBps { get; set; }

    /// <summary>
    /// Percentage change in read speed between sanitizations.
    /// </summary>
    public double? ReadSpeedChangePercent { get; set; }

    /// <summary>
    /// Error count from first sanitization.
    /// </summary>
    public int? Sanitize1Errors { get; set; }

    /// <summary>
    /// Error count from second sanitization.
    /// </summary>
    public int? Sanitize2Errors { get; set; }

    /// <summary>
    /// Human-readable summary of SMART attribute changes across the test.
    /// </summary>
    public string? SmartDeltaSummary { get; set; }

    /// <summary>
    /// Whether certified disk is recommended for use
    /// </summary>
    public bool Recommended { get; set; }
    
    /// <summary>
    /// Recommendation reason
    /// </summary>
    public string? RecommendationNotes { get; set; }
}

/// <summary>
/// SMART attribute summary for certificate
/// </summary>
public class SmartAttributeSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
}

/// <summary>
/// Certificate status
/// </summary>
public enum CertificateStatus
{
    Active,
    Expired,
    Revoked,
    Draft
}

/// <summary>
/// Disk archive record for removed disks
/// </summary>
public class DiskArchive
{
    public int Id { get; set; }
    
    /// <summary>
    /// Disk card that was archived
    /// </summary>
    public int DiskCardId { get; set; }
    public DiskCard? DiskCard { get; set; }
    
    /// <summary>
    /// When archived
    /// </summary>
    public DateTime ArchivedAt { get; set; }
    
    /// <summary>
    /// Reason for archiving
    /// </summary>
    public ArchiveReason Reason { get; set; }
    
    /// <summary>
    /// Additional notes
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Who archived it
    /// </summary>
    public string ArchivedBy { get; set; } = string.Empty;
    
    /// <summary>
    /// Brief summary for archive listing
    /// </summary>
    public string Summary { get; set; } = string.Empty;
    
    /// <summary>
    /// Final grade before archive
    /// </summary>
    public string FinalGrade { get; set; } = string.Empty;
    
    /// <summary>
    /// Final score before archive
    /// </summary>
    public double FinalScore { get; set; }
    
    /// <summary>
    /// Total tests performed
    /// </summary>
    public int TotalTests { get; set; }
    
    /// <summary>
    /// Whether can be restored
    /// </summary>
    public bool CanRestore { get; set; } = true;
}

/// <summary>
/// Reason for archiving
/// </summary>
public enum ArchiveReason
{
    Failed,
    Sold,
    Donated,
    Recycled,
    Replaced,
    UserRequest,
    Other
}