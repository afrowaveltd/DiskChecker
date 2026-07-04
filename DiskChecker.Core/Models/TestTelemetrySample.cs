namespace DiskChecker.Core.Models;

/// <summary>
/// Phase/type of a persisted telemetry sample.
/// Kept generic enough for later analysis UI and cross-test comparisons.
/// </summary>
public enum TelemetrySamplePhase
{
    Unknown = 0,
    Write = 1,
    Read = 2,
    Verify = 3,
    Sanitize1Write = 10,
    Sanitize1Read = 11,
    Sanitize2Write = 12,
    Sanitize2Read = 13
}

/// <summary>
/// Persisted throughput telemetry sample used for later analysis.
/// Unlike certificate profile points, this is research/diagnostic data: it preserves
/// time, progress, bytes processed, stall markers and retention reason.
/// </summary>
public class TestTelemetrySample
{
    public long Id { get; set; }
    public int TestSessionId { get; set; }
    public TestSession? TestSession { get; set; }
    public TelemetrySamplePhase Phase { get; set; }
    public int SequenceIndex { get; set; }
    public DateTime TimestampUtc { get; set; }
    public double? ElapsedMs { get; set; }
    public double ProgressPercent { get; set; }
    public long BytesProcessed { get; set; }
    public double SpeedMBps { get; set; }
    public bool IsStalled { get; set; }
    public bool IsAnomaly { get; set; }
    public string? RetentionReason { get; set; }
}
