using System;

namespace DiskChecker.Core.Models;

/// <summary>
/// Report model for test results and history display.
/// </summary>
public class TestReport
{
    /// <summary>
    /// Unique identifier for the report.
    /// </summary>
    public Guid ReportId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// When the test was performed.
    /// </summary>
    public DateTime TestDate { get; set; }

    /// </// <summary>
    /// Type of test performed (SMART, Surface, Sanitization, etc.).
    /// </summary>
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
    /// Drive model name.
    /// </summary>
    public string DriveModel { get; set; } = string.Empty;

    /// <summary>
    /// Drive serial number.
    /// </>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>
    /// Average speed in MB/s.
    /// </summary>
    public double AverageSpeed { get; set; }

    /// <summary>
    /// Peak speed in MB/s.
    /// </summary>
    public double PeakSpeed { get; set; }

    /// <summary>
    /// Number of errors detected.
    /// </summary>
    public int Errors { get; set; }

    /// <summary>
    /// Whether the test completed successfully.
    /// </summary>
    public bool IsCompleted { get; set; }
}