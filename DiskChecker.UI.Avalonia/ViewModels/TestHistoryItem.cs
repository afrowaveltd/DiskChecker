using System.Globalization;
using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Represents a test history item for display in the UI.
/// </summary>
public class TestHistoryItem : ObservableObject
{
    private bool _isSelected;

    public int Id { get; set; }
    public string DriveName { get; set; } = string.Empty;
    public string DrivePath { get; set; } = string.Empty;
    public string TestType { get; set; } = string.Empty;
    public DateTime TestDate { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;
    public string DurationText => Duration.ToString(@"hh\:mm\:ss");
    public string Status { get; set; } = string.Empty;
    public bool IsPassed { get; set; }
    public int ErrorCount { get; set; }
    public long BytesTested { get; set; }
    public double AverageSpeed { get; set; }
    public double PeakSpeed { get; set; }
    public string QualityGrade { get; set; } = string.Empty;
    public double QualityScore { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string SpeedText => AverageSpeed > 0 
        ? $"{AverageSpeed:F1} MB/s" 
        : "N/A";

    public string StatusIcon => Status.ToUpperInvariant() switch
    {
        "PASSED" or "SUCCESS" => "✓",
        "FAILED" or "ERROR" => "✗",
        "RUNNING" => "⟳",
        "CANCELLED" => "⊘",
        _ => "?"
    };

    public string StatusColor => Status.ToUpperInvariant() switch
    {
        "PASSED" or "SUCCESS" => "Green",
        "FAILED" or "ERROR" => "Red",
        "RUNNING" => "Orange",
        "CANCELLED" => "Gray",
        _ => "Gray"
    };
}