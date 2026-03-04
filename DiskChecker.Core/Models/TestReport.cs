namespace DiskChecker.Core.Models;

/// <summary>
/// Represents a complete test report with all analytics and SMART data.
/// Used for historical records and comparisons.
/// </summary>
public class TestReport
{
    /// <summary>
    /// Unique identifier for this report.
    /// </summary>
    public Guid ReportId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Reference to the disk card this test was performed on.
    /// </summary>
    public Guid DiskCardId { get; set; }

    /// <summary>
    /// Navigation property to DiskCard.
    /// </summary>
    public virtual DiskCard? DiskCard { get; set; }

    /// <summary>
    /// Date when the test was performed.
    /// </summary>
    public DateTime TestDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Type of test performed (Surface, SMART, Sequential, etc.).
    /// </summary>
    public string TestType { get; set; } = "Surface";

    /// <summary>
    /// Duration of the test.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Overall health grade at time of test.
    /// </summary>
    public string Grade { get; set; } = "F";

    /// <summary>
    /// Numeric health score (0-100).
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Health assessment text (e.g., "Excellent", "Poor").
    /// </summary>
    public string HealthAssessment { get; set; } = string.Empty;

    /// <summary>
    /// Number of errors detected during test.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Percentage of anomalies detected.
    /// </summary>
    public double AnomalyPercentage { get; set; }

    /// <summary>
    /// Average throughput in MB/s.
    /// </summary>
    public double AverageThroughputMbps { get; set; }

    /// <summary>
    /// Peak throughput in MB/s.
    /// </summary>
    public double PeakThroughputMbps { get; set; }

    /// <summary>
    /// Minimum throughput in MB/s (filtered).
    /// </summary>
    public double MinThroughputMbps { get; set; }

    /// <summary>
    /// Total bytes tested.
    /// </summary>
    public long TotalBytesTested { get; set; }

    /// <summary>
    /// Temperature at end of test (°C).
    /// </summary>
    public int? FinalTemperatureCelsius { get; set; }

    /// <summary>
    /// Power on hours at time of test.
    /// </summary>
    public long? PowerOnHours { get; set; }

    /// <summary>
    /// JSON-serialized speed samples for graph plotting.
    /// </summary>
    public string? SpeedSamplesJson { get; set; }

    /// <summary>
    /// Complete SMART data snapshot (JSON).
    /// </summary>
    public string? SmartDataJson { get; set; }

    /// <summary>
    /// Raw surface test result ID for reference.
    /// </summary>
    public Guid? SurfaceTestResultId { get; set; }

    /// <summary>
    /// Raw SMART check result ID for reference.
    /// </summary>
    public Guid? SmartCheckResultId { get; set; }

    /// <summary>
    /// Notes or observations from this test.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Whether this report was printed/exported.
    /// </summary>
    public bool IsExported { get; set; }

    /// <summary>
    /// Date when report was last printed/exported.
    /// </summary>
    public DateTime? LastExportDate { get; set; }
}
