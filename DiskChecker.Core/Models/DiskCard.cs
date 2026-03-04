namespace DiskChecker.Core.Models;

/// <summary>
/// Represents a disk card (permanent record of a tested disk in the database).
/// </summary>
public class DiskCard
{
    /// <summary>
    /// Unique identifier for the disk card.
    /// </summary>
    public Guid DiskCardId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Disk serial number (unique identifier from disk itself).
    /// </summary>
    public required string SerialNumber { get; set; }

    /// <summary>
    /// Disk model name.
    /// </summary>
    public required string ModelName { get; set; }

    /// <summary>
    /// Disk manufacturer.
    /// </summary>
    public string? Manufacturer { get; set; }

    /// <summary>
    /// Disk interface type (SATA, NVMe, etc.).
    /// </summary>
    public string? InterfaceType { get; set; }

    /// <summary>
    /// Total disk capacity in bytes.
    /// </summary>
    public long CapacityBytes { get; set; }

    /// <summary>
    /// Date when disk card was created (first test).
    /// </summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last test performed on this disk.
    /// </summary>
    public DateTime? LastTestedDate { get; set; }

    /// <summary>
    /// Current status of the disk (OK, WARNING, FAILED, etc.).
    /// </summary>
    public string Status { get; set; } = "OK";

    /// <summary>
    /// Number of times this disk has been tested.
    /// </summary>
    public int TestCount { get; set; }

    /// <summary>
    /// Current health grade (A, B, C, D, F).
    /// </summary>
    public string? CurrentGrade { get; set; }

    /// <summary>
    /// Current health score (0-100).
    /// </summary>
    public int? CurrentScore { get; set; }

    /// <summary>
    /// Notes or observations about this disk.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Collection of all surface tests performed on this disk.
    /// </summary>
    public virtual ICollection<SurfaceTestResult> SurfaceTests { get; set; } = new List<SurfaceTestResult>();

    /// <summary>
    /// Collection of all SMART checks performed on this disk.
    /// </summary>
    public virtual ICollection<SmartCheckResult> SmartChecks { get; set; } = new List<SmartCheckResult>();

    /// <summary>
    /// Collection of all test reports (historical records) for this disk.
    /// </summary>
    public virtual ICollection<TestReport> TestReports { get; set; } = new List<TestReport>();
}
