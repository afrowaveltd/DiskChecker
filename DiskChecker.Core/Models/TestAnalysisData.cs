namespace DiskChecker.Core.Models;

/// <summary>
/// Complete persisted data bundle for later test analysis UI.
/// This is intentionally read-oriented and combines session metadata, throughput
/// telemetry, detected anomalies and complete seek samples.
/// </summary>
public class TestAnalysisData
{
    public TestSession Session { get; set; } = new();
    public List<TestTelemetrySample> TelemetrySamples { get; set; } = new();
    public List<TestAnomalyEvent> AnomalyEvents { get; set; } = new();
    public List<TestStallEvent> StallEvents { get; set; } = new();
    public List<SeekSampleRecord> SeekSamples { get; set; } = new();
    public List<TemperatureSample> TemperatureSamples { get; set; } = new();
    public SmartAnalysisReport SmartReport { get; set; } = new();

    public bool HasThroughputTelemetry => TelemetrySamples.Count > 0;
    public bool HasAnomalies => AnomalyEvents.Count > 0;
    public bool HasStalls => StallEvents.Count > 0;
    public bool HasSeekSamples => SeekSamples.Count > 0;
}

/// <summary>
/// Lightweight summary for listing available test records in the future analysis workspace.
/// </summary>
public class TestAnalysisSummary
{
    public int TestSessionId { get; set; }
    public int DiskCardId { get; set; }
    public string DiskModel { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public TestType TestType { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public string Grade { get; set; } = "?";
    public double Score { get; set; }
    public int TelemetrySampleCount { get; set; }
    public int AnomalyCount { get; set; }
    public int StallCount { get; set; }
    public int SeekSampleCount { get; set; }
}
