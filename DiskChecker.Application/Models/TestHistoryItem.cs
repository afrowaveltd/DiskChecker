using DiskChecker.Core.Models;

namespace DiskChecker.Application.Models;

/// <summary>
/// History item for display in UI lists.
/// </summary>
public class TestHistoryItem
{
    public Guid TestId { get; set; }
    public Guid DriveId { get; set; }
    public string DriveName { get; set; } = string.Empty;
    public string DrivePath { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime TestDate { get; set; }
    public string TestType { get; set; } = string.Empty;
    public QualityGrade Grade { get; set; }
    public int Score { get; set; }
    public double AverageSpeed { get; set; }
    public double PeakSpeed { get; set; }
    public double MinSpeed { get; set; }
    public long TotalBytesTested { get; set; }
    public int ErrorCount { get; set; }
    
    // SMART data snapshot
    public SmartaData? SmartaData { get; set; }
    
    // Speed samples
    public List<SurfaceTestSample> SurfaceSamples { get; set; } = new();
    
    // Legacy properties for backward compatibility
    public int TotalItems { get; set; }
    public int PageSize { get; set; }
    public int PageIndex { get; set; }
}