namespace DiskChecker.Core.Models;

/// <summary>
/// Persisted seek latency sample. Seek tests are small enough (usually <= 3000 seeks)
/// to keep every measured operation for later research and detailed charting.
/// </summary>
public class SeekSampleRecord
{
    public long Id { get; set; }
    public int TestSessionId { get; set; }
    public TestSession? TestSession { get; set; }
    public SeekTestType TestType { get; set; }
    public int Index { get; set; }
    public long SourceLba { get; set; }
    public long DestinationLba { get; set; }
    public long SeekDistance { get; set; }
    public double LatencyMs { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
}
