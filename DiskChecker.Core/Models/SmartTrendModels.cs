namespace DiskChecker.Core.Models;

/// <summary>
/// A single historical SMART snapshot stored in the dedicated SmartSnapshots table.
/// Enables efficient trend queries across all tests for a disk.
/// </summary>
public class SmartSnapshotRecord
{
    public int Id { get; set; }
    
    /// <summary>Foreign key to DiskCard.</summary>
    public int DiskCardId { get; set; }
    
    /// <summary>Foreign key to TestSession (nullable for manual snapshots).</summary>
    public int? TestSessionId { get; set; }
    
    /// <summary>When this snapshot was taken (UTC).</summary>
    public DateTime RetrievedAtUtc { get; set; }
    
    // === Basic health ===
    public bool IsHealthy { get; set; }
    public bool IsFailing { get; set; }
    
    // === Basic metrics ===
    public int? Temperature { get; set; }
    public int? PowerOnHours { get; set; }
    public int PowerCycleCount { get; set; }
    
    // === ATA/SATA ===
    public int? ReallocatedSectorCount { get; set; }
    public int? PendingSectorCount { get; set; }
    public int? UncorrectableErrorCount { get; set; }
    public int? WearLevelingCount { get; set; }
    
    // === NVMe ===
    public int? AvailableSpare { get; set; }
    public int? PercentageUsed { get; set; }
    public int? MediaErrors { get; set; }
    public int? UnsafeShutdowns { get; set; }
    
    // === Navigation ===
    public DiskCard? DiskCard { get; set; }
    public TestSession? TestSession { get; set; }
}

/// <summary>
/// Computed trend data for a single SMART metric over time.
/// </summary>
public class SmartMetricTrend
{
    public string MetricName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public List<SmartTrendPoint> Points { get; set; } = new();
    
    /// <summary>Rate of change per day (positive = worsening).</summary>
    public double? RatePerDay { get; set; }
    
    /// <summary>Projected days until critical threshold.</summary>
    public double? DaysUntilCritical { get; set; }
    
    /// <summary>Whether this trend shows significant degradation.</summary>
    public bool IsDegrading => RatePerDay > 0 && DaysUntilCritical < 365;
    
    /// <summary>Human-readable trend description.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// A single data point in a SMART metric trend.
/// </summary>
public class SmartTrendPoint
{
    public DateTime Timestamp { get; set; }
    public double? Value { get; set; }
    public string? Label { get; set; }
}

/// <summary>
/// Complete trend analysis for a disk.
/// </summary>
public class SmartTrendReport
{
    public int DiskCardId { get; set; }
    public string DiskModel { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public int SnapshotCount { get; set; }
    public DateTime? FirstSnapshot { get; set; }
    public DateTime? LastSnapshot { get; set; }
    
    /// <summary>Vendor-specific wear assessment.</summary>
    public WearAssessment? WearAssessment { get; set; }
    
    /// <summary>Trends for key metrics.</summary>
    public List<SmartMetricTrend> Trends { get; set; } = new();
    
    /// <summary>Overall trend health summary.</summary>
    public string Summary { get; set; } = string.Empty;
    
    /// <summary>Whether any trend is critical.</summary>
    public bool HasCriticalTrends => Trends.Any(t => t.IsDegrading);
}

/// <summary>
/// Data for rendering a trend chart in the analysis workspace.
/// </summary>
public class SmartTrendChartData
{
    public string Title { get; set; } = string.Empty;
    public string YAxisLabel { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public List<SmartTrendPoint> Points { get; set; } = new();
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public double? WarningThreshold { get; set; }
    public double? CriticalThreshold { get; set; }
    
    /// <summary>Polyline points for SVG rendering (x,y pairs).</summary>
    public string PolylinePoints { get; set; } = string.Empty;
}
