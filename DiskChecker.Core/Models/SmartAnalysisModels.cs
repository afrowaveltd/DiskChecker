namespace DiskChecker.Core.Models;

public enum SmartAnalysisSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public class SmartAnalysisSnapshot
{
    public DateTime? RetrievedAtUtc { get; set; }
    public bool IsHealthy { get; set; }
    public bool IsFailing { get; set; }
    public string FailurePrediction { get; set; } = string.Empty;
    public int? Temperature { get; set; }
    public int? PowerOnHours { get; set; }
    public int PowerCycleCount { get; set; }
    public int? ReallocatedSectorCount { get; set; }
    public int? PendingSectorCount { get; set; }
    public int? UncorrectableErrorCount { get; set; }
    public int? WearLevelingCount { get; set; }
    public int? AvailableSpare { get; set; }
    public int? PercentageUsed { get; set; }
    public int? MediaErrors { get; set; }
    public int? UnsafeShutdowns { get; set; }
}

public class SmartDeltaItem
{
    public string Name { get; set; } = string.Empty;
    public long? Before { get; set; }
    public long? After { get; set; }
    public long? Delta { get; set; }
    public SmartAnalysisSeverity Severity { get; set; }
    public string Note { get; set; } = string.Empty;
}

public class SmartAnalysisReport
{
    public SmartAnalysisSnapshot? Before { get; set; }
    public SmartAnalysisSnapshot? After { get; set; }
    public List<SmartDeltaItem> Deltas { get; set; } = new();
    public List<SmartDeltaItem> WearIndicators { get; set; } = new();
    public string Summary { get; set; } = "SMART data nejsou k dispozici.";
    public bool HasCriticalChanges => Deltas.Any(d => d.Severity == SmartAnalysisSeverity.Critical) || WearIndicators.Any(d => d.Severity == SmartAnalysisSeverity.Critical);
    public bool HasWarnings => HasCriticalChanges || Deltas.Any(d => d.Severity == SmartAnalysisSeverity.Warning) || WearIndicators.Any(d => d.Severity == SmartAnalysisSeverity.Warning);
}
