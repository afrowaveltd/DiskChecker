namespace DiskChecker.Core.Models;

/// <summary>
/// Persisted interval where the device was observed as stalled/unresponsive.
/// This complements point samples so later analysis can show real time spent frozen,
/// not only a single 0 MB/s dip on a progress chart.
/// </summary>
public class TestStallEvent
{
    public long Id { get; set; }
    public int TestSessionId { get; set; }
    public TestSession? TestSession { get; set; }
    public TelemetrySamplePhase Phase { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }
    public double DurationMs { get; set; }
    public double StartProgressPercent { get; set; }
    public double EndProgressPercent { get; set; }
    public long BytesProcessed { get; set; }
    public double? LastSpeedBeforeStallMBps { get; set; }
    public double? FirstSpeedAfterStallMBps { get; set; }
}
