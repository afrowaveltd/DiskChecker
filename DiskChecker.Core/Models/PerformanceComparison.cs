using System;

namespace DiskChecker.Core.Models;

/// <summary>
/// Detailed performance comparison between two disks.
/// </summary>
public class PerformanceComparison
{
    /// <summary>
    /// First disk in comparison.
    /// </summary>
    public DiskCard Disk1 { get; set; } = null!;

    /// <summary>
    /// Second disk in comparison.
    /// </summary>
    public DiskCard Disk2 { get; set; } = null!;

    /// <summary>
    /// Difference in write speed (Disk1 - Disk2) in MB/s.
    /// </summary>
    public double WriteSpeedDifference { get; set; }

    /// <summary>
    /// Difference in read speed (Disk1 - Disk2) in MB/s.
    /// </summary>
    public double ReadSpeedDifference { get; set; }

    /// <summary>
    /// Speed advantage percentage of the faster disk.
    /// </summary>
    public int SpeedAdvantagePercent { get; set; }

    /// <summary>
    /// Name of the faster disk.
    /// </summary>
    public string FasterDisk { get; set; } = string.Empty;

    /// <summary>
    /// Name of the more reliable disk based on score.
    /// </summary>
    public string MoreReliableDisk { get; set; } = string.Empty;

    /// <summary>
    /// Name of the recommended disk.
    /// </summary>
    public string RecommendedDisk { get; set; } = string.Empty;

    /// <summary>
    /// Summary of comparison results.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}