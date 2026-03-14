using System;

namespace DiskChecker.Core.Models;

/// <summary>
/// Historical test record for detailed history tracking.
/// </summary>
public class HistoricalTest
{
    /// <summary>
    /// Unique identifier for the test.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Drive serial number.
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Drive model name.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// When the test was performed.
    /// </summary>
    public DateTime TestDate { get; set; }

    /// <summary>
    /// Type of test performed.
    /// </>
    public string TestType { get; set; } = string.Empty;

    /// <summary>
    /// Grade assigned to the test (A-F).
    /// </summary>
    public string Grade { get; set; } = "F";

    /// <summary>
    /// Numeric score (0-100).
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Number of errors detected.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Average throughput in MB/s.
    /// </summary>
    public double AverageThroughputMbps { get; set; }

    /// <summary>
    /// Peak throughput in MB/s.
    /// </summary>
    public double PeakThroughputMbps { get; set; }

    /// <summary>
    /// Total bytes tested.
    /// </summary>
    public long TotalBytesTested { get; set; }
    
    /// <summary>
    /// Health assessment status (e.g., "Healthy", "Warning", "Critical").
    /// </summary>
    public string HealthAssessment { get; set; } = "Unknown";
    
    /// <summary>
    /// Duration of the test in seconds.
    /// </summary>
    public double Duration { get; set; }
    
    /// <summary>
    /// Additional notes or comments about the test.
    /// </summary>
    public string? Notes { get; set; }
}