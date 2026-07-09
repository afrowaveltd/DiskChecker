using System.ComponentModel.DataAnnotations.Schema;

namespace DiskChecker.Core.Models;

/// <summary>
/// Speed sample for test speed tracking.
/// </summary>
public class SpeedSample
{
    public DateTime Timestamp { get; set; }
    public double SpeedMBps { get; set; }
    public double ProgressPercent { get; set; }
    public long BytesProcessed { get; set; }

    /// <summary>
    /// Elapsed time from the beginning of the test when this sample was captured.
    /// Kept out of the legacy owned sample tables; detailed persisted telemetry stores ElapsedMs.
    /// </summary>
    [NotMapped]
    public TimeSpan? Elapsed { get; set; }

    /// <summary>
    /// Logical phase for this sample (for example Write, Read, Verify).
    /// </summary>
    [NotMapped]
    public string? Phase { get; set; }

    /// <summary>
    /// True if the device was stalled (unresponsive) when this sample was recorded.
    /// Stalled samples show as red markers on the certificate chart.
    /// </summary>
    public bool IsStalled { get; set; }
}