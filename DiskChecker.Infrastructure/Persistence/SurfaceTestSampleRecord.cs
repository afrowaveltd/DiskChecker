namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// Database record for surface test samples.
/// </summary>
public class SurfaceTestSampleRecord
{
    public Guid Id { get; set; }
    public Guid TestId { get; set; }
    public double ThroughputMbps { get; set; }
    public int Temperature { get; set; }
    public int ErrorCount { get; set; }
    public long BytesProcessed { get; set; }
    public long OffsetBytes { get; set; }
    public long BlockSizeBytes { get; set; }
    public DateTime? TimestampUtc { get; set; }

    // Navigation property
    public TestRecord Test { get; set; } = null!;
}