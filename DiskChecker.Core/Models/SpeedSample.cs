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
    /// True if the device was stalled (unresponsive) when this sample was recorded.
    /// Stalled samples show as red markers on the certificate chart.
    /// </summary>
    public bool IsStalled { get; set; }
}