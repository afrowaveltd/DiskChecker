using System;

namespace DiskChecker.Core.Models;

/// <summary>
/// Result of disk comparison for ranking and recommendations.
/// </summary>
public class DiskComparisonResult
{
    /// <summary>
    /// The disk card being compared.
    /// </summary>
    public DiskCard Disk { get; set; } = null!;

    /// <summary>
    /// Rank position in comparison (1 = best).
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Overall score (0-100).
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Grade assigned (A-F or ?).
    /// </summary>
    public string Grade { get; set; } = "?";

    /// <summary>
    /// Average write speed in MB/s.
    /// </summary>
    public double AvgWriteSpeed { get; set; }

    /// <summary>
    /// Average read speed in MB/s.
    /// </summary>
    public double AvgReadSpeed { get; set; }

    /// <summary>
    /// Total error count from test.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Recommendation based on test results.
    /// </summary>
    public string Recommendation { get; set; } = string.Empty;
}