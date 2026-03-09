namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// Database record for SMART data.
/// </summary>
public class SmartaRecord
{
    public Guid Id { get; set; }
    public Guid TestId { get; set; }
    public int PowerOnHours { get; set; }
    public long ReallocatedSectorCount { get; set; }
    public long PendingSectorCount { get; set; }
    public long UncorrectableErrorCount { get; set; }
    public double Temperature { get; set; }
    public int? WearLevelingCount { get; set; }

    // Navigation property
    public TestRecord Test { get; set; } = null!;
}