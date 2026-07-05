using System;
using System.Collections.Generic;
using System.Linq;

namespace DiskChecker.Core.Models;

/// <summary>
/// Represents a detected performance anomaly with high-resolution samples
/// for detailed analysis and overlay comparison.
/// </summary>
public class SpeedAnomaly
{
    /// <summary>Unique identifier.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the parent test session.</summary>
    public int TestSessionId { get; set; }

    /// <summary>Whether this anomaly occurred during Write or Read phase.</summary>
    public string Phase { get; set; } = "Write";

    /// <summary>Index in the standard (downsampled) sample timeline where anomaly starts.</summary>
    public int StartStandardIndex { get; set; }

    /// <summary>Index in the standard (downsampled) sample timeline where anomaly ends.</summary>
    public int EndStandardIndex { get; set; }

    /// <summary>Progress percentage at anomaly start (0-100).</summary>
    public double StartProgressPercent { get; set; }

    /// <summary>Progress percentage at anomaly end (0-100).</summary>
    public double EndProgressPercent { get; set; }

    /// <summary>Byte offset / processed-byte position at anomaly start.</summary>
    public long StartBytesProcessed { get; set; }

    /// <summary>Byte offset / processed-byte position at anomaly end.</summary>
    public long EndBytesProcessed { get; set; }

    /// <summary>Approximate 512-byte LBA at anomaly start, derived from bytes processed.</summary>
    public long StartLba512 { get; set; }

    /// <summary>Approximate 512-byte LBA at anomaly end, derived from bytes processed.</summary>
    public long EndLba512 { get; set; }

    /// <summary>Duration of the anomaly in milliseconds.</summary>
    public double DurationMs { get; set; }

    /// <summary>Minimum speed during anomaly (MB/s).</summary>
    public double MinSpeedMBps { get; set; }

    /// <summary>Maximum speed during anomaly (MB/s).</summary>
    public double MaxSpeedMBps { get; set; }

    /// <summary>Average speed during anomaly (MB/s).</summary>
    public double AvgSpeedMBps { get; set; }

    /// <summary>Speed at anomaly entry (MB/s) — the "before" baseline.</summary>
    public double EntrySpeedMBps { get; set; }

    /// <summary>Speed at anomaly exit (MB/s) — the "after" recovery speed.</summary>
    public double ExitSpeedMBps { get; set; }

    /// <summary>Maximum deviation from baseline as percentage.</summary>
    public double MaxDeviationPercent { get; set; }

    /// <summary>
    /// High-resolution samples captured during the anomaly (100ms intervals).
    /// Stored as JSON in the database.
    /// </summary>
    public List<SpeedSample> HighResSamples { get; set; } = new();

    /// <summary>
    /// Group key for matching write/read anomalies at the same disk position.
    /// Anomalies with the same OverlayGroup can be overlaid for comparison.
    /// </summary>
    public string? OverlayGroup { get; set; }

    /// <summary>
    /// Whether this anomaly correlates with a known disk defect (bad sector, weak head, etc.).
    /// </summary>
    public string? DefectType { get; set; }

    /// <summary>
    /// Severity score 0-100 (higher = more severe).
    /// </summary>
    public double SeverityScore { get; set; }

    /// <summary>
    /// Computes severity based on deviation magnitude and duration.
    /// </summary>
    public void ComputeSeverity()
    {
        // Severity = deviation magnitude (0-50) + duration factor (0-50)
        var deviationScore = Math.Min(MaxDeviationPercent, 100) * 0.5;
        var durationScore = Math.Min(DurationMs / 100.0, 50); // 5s+ = max
        SeverityScore = Math.Round(Math.Min(deviationScore + durationScore, 100), 1);
    }

    /// <summary>
    /// Returns a compact summary string for display.
    /// </summary>
    public string ToSummary()
    {
        var direction = MinSpeedMBps < EntrySpeedMBps ? "↓" : "↑";
        return $"{Phase} {direction}{MaxDeviationPercent:F0}% " +
               $"({MinSpeedMBps:F0}–{MaxSpeedMBps:F0} MB/s, {DurationMs / 1000:F1}s) " +
               $"@{StartProgressPercent:F0}–{EndProgressPercent:F0}% " +
               $"LBA~{StartLba512:N0}–{EndLba512:N0}";
    }
}
