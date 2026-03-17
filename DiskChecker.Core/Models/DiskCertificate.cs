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