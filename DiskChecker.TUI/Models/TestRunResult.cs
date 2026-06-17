namespace DiskChecker.TUI.Models;

/// <summary>
/// Result of a disk test run.
/// </summary>
public sealed class TestRunResult
{
    public string DiskModel { get; set; } = string.Empty;
    public string DiskSerial { get; set; } = string.Empty;
    public string DevicePath { get; set; } = string.Empty;
    public ulong CapacityBytes { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public TimeSpan Duration => CompletedAt - StartedAt;

    public List<SpeedSample> WriteSamples { get; set; } = new();
    public List<SpeedSample> ReadSamples { get; set; } = new();
    public List<SpeedSample>? SeekSamples { get; set; }

    public double WriteSpeedAvgMBps { get; set; }
    public double WriteSpeedMinMBps { get; set; }
    public double WriteSpeedMaxMBps { get; set; }

    public double ReadSpeedAvgMBps { get; set; }
    public double ReadSpeedMinMBps { get; set; }
    public double ReadSpeedMaxMBps { get; set; }

    public double? SeekAvgMs { get; set; }
    public double? SeekMinMs { get; set; }
    public double? SeekMaxMs { get; set; }

    public double? MaxTemperatureC { get; set; }
    public double? AvgTemperatureC { get; set; }

    public bool SanitizationPassed { get; set; }
    public string? SanitizationMethod { get; set; }
    public string? SanitizationOutput { get; set; }

    public string? ErrorMessage { get; set; }
    public bool Success => string.IsNullOrEmpty(ErrorMessage);

    public string Grade
    {
        get
        {
            if (!Success) return "F";
            double score = (WriteSpeedAvgMBps + ReadSpeedAvgMBps) / 2.0;
            if (score >= 200) return "A";
            if (score >= 150) return "B";
            if (score >= 100) return "C";
            if (score >= 50) return "D";
            return "E";
        }
    }
}
