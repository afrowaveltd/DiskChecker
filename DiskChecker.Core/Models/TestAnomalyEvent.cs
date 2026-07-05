namespace DiskChecker.Core.Models;

/// <summary>
/// Persisted performance anomaly summary for later analysis UI.
/// High-resolution samples remain in TestTelemetrySamples and are marked by IsAnomaly.
/// </summary>
public class TestAnomalyEvent
{
    public long Id { get; set; }
    public int TestSessionId { get; set; }
    public TestSession? TestSession { get; set; }
    public TelemetrySamplePhase Phase { get; set; }
    public int StartStandardIndex { get; set; }
    public int EndStandardIndex { get; set; }
    public double StartProgressPercent { get; set; }
    public double EndProgressPercent { get; set; }
    public long StartBytesProcessed { get; set; }
    public long EndBytesProcessed { get; set; }
    public long StartLba512 { get; set; }
    public long EndLba512 { get; set; }
    public double DurationMs { get; set; }
    public double MinSpeedMBps { get; set; }
    public double MaxSpeedMBps { get; set; }
    public double AvgSpeedMBps { get; set; }
    public double EntrySpeedMBps { get; set; }
    public double ExitSpeedMBps { get; set; }
    public double MaxDeviationPercent { get; set; }
    public double SeverityScore { get; set; }
    public string? OverlayGroup { get; set; }
    public string? DefectType { get; set; }
}
