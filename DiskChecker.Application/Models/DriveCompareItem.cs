using DiskChecker.Core.Models;

namespace DiskChecker.Application.Models;

/// <summary>
/// Item for drive comparison display.
/// </summary>
public class DriveCompareItem
{
    public Guid DriveId { get; set; }
    public string DriveName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int TotalTests { get; set; }
    public DateTime? LastTestDate { get; set; }
    public QualityGrade? LastGrade { get; set; }
}