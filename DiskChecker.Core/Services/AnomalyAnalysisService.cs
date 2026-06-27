using System;
using System.Collections.Generic;
using System.Linq;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Service for analyzing speed anomalies across test phases.
/// Enables overlay comparison of write vs read anomalies at the same disk position,
/// pattern detection, and intelligent disk health scoring based on anomaly characteristics.
/// </summary>
public class AnomalyAnalysisService
{
    /// <summary>
    /// Matches write and read anomalies that overlap in disk position (progress %).
    /// Returns pairs of (write, read) anomalies for overlay comparison.
    /// </summary>
    public List<(SpeedAnomaly Write, SpeedAnomaly Read)> FindOverlappingAnomalies(
        List<SpeedAnomaly> anomalies)
    {
        var writeAnomalies = anomalies.Where(a => a.Phase == "Write").OrderBy(a => a.StartProgressPercent).ToList();
        var readAnomalies = anomalies.Where(a => a.Phase == "Read").OrderBy(a => a.StartProgressPercent).ToList();
        var pairs = new List<(SpeedAnomaly Write, SpeedAnomaly Read)>();

        foreach (var write in writeAnomalies)
        {
            // Find read anomalies that overlap in progress range
            var overlapping = readAnomalies
                .Where(r => Overlaps(write.StartProgressPercent, write.EndProgressPercent,
                                     r.StartProgressPercent, r.EndProgressPercent))
                .OrderBy(r => Math.Abs(r.StartProgressPercent - write.StartProgressPercent))
                .FirstOrDefault();

            if (overlapping != null)
            {
                pairs.Add((write, overlapping));
                readAnomalies.Remove(overlapping);
            }
        }

        return pairs;
    }

    /// <summary>
    /// Computes a correlation score (0-100) between a write and read anomaly.
    /// High score = anomalies are likely caused by the same physical defect.
    /// </summary>
    public double ComputeCorrelationScore(SpeedAnomaly write, SpeedAnomaly read)
    {
        double score = 0;

        // Position overlap (0-40 points)
        var overlapStart = Math.Max(write.StartProgressPercent, read.StartProgressPercent);
        var overlapEnd = Math.Min(write.EndProgressPercent, read.EndProgressPercent);
        var overlapRange = overlapEnd - overlapStart;
        var totalRange = Math.Max(write.EndProgressPercent, read.EndProgressPercent) -
                         Math.Min(write.StartProgressPercent, read.StartProgressPercent);
        if (totalRange > 0)
            score += (overlapRange / totalRange) * 40;

        // Similar deviation magnitude (0-30 points)
        var deviationRatio = Math.Min(write.MaxDeviationPercent, read.MaxDeviationPercent) /
                             Math.Max(write.MaxDeviationPercent, read.MaxDeviationPercent);
        score += deviationRatio * 30;

        // Similar duration (0-20 points)
        var durationRatio = Math.Min(write.DurationMs, read.DurationMs) /
                            Math.Max(write.DurationMs, read.DurationMs);
        score += durationRatio * 20;

        // Both show same direction (drop or spike) (0-10 points)
        var writeDrop = write.MinSpeedMBps < write.EntrySpeedMBps;
        var readDrop = read.MinSpeedMBps < read.EntrySpeedMBps;
        if (writeDrop == readDrop)
            score += 10;

        return Math.Round(score, 1);
    }

    /// <summary>
    /// Detects repeating anomaly patterns (same position, multiple occurrences).
    /// Returns groups of anomalies that repeat at similar disk positions.
    /// </summary>
    public List<List<SpeedAnomaly>> FindRepeatingPatterns(
        List<SpeedAnomaly> anomalies,
        double positionTolerancePercent = 5.0)
    {
        var groups = new List<List<SpeedAnomaly>>();
        var remaining = anomalies.OrderBy(a => a.StartProgressPercent).ToList();

        while (remaining.Count > 0)
        {
            var current = remaining[0];
            remaining.RemoveAt(0);

            var group = new List<SpeedAnomaly> { current };

            // Find all anomalies at similar positions
            var similar = remaining
                .Where(a => Math.Abs(a.StartProgressPercent - current.StartProgressPercent) <= positionTolerancePercent)
                .ToList();

            foreach (var s in similar)
            {
                group.Add(s);
                remaining.Remove(s);
            }

            if (group.Count > 1)
                groups.Add(group);
        }

        return groups;
    }

    /// <summary>
    /// Generates a human-readable analysis report from anomalies.
    /// </summary>
    public string GenerateAnomalyReport(List<SpeedAnomaly> anomalies)
    {
        if (anomalies.Count == 0)
            return "✅ Nebyly detekovány žádné výkonové anomálie.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📊 Detekováno {anomalies.Count} výkonových anomálií:");
        sb.AppendLine();

        var writeAnomalies = anomalies.Where(a => a.Phase == "Write").ToList();
        var readAnomalies = anomalies.Where(a => a.Phase == "Read").ToList();

        if (writeAnomalies.Count > 0)
        {
            sb.AppendLine($"✍️ Zápis ({writeAnomalies.Count}):");
            foreach (var a in writeAnomalies.OrderBy(a => a.StartProgressPercent))
                sb.AppendLine($"   • {a.ToSummary()} (závažnost: {a.SeverityScore:F0}/100)");
            sb.AppendLine();
        }

        if (readAnomalies.Count > 0)
        {
            sb.AppendLine($"👁️ Čtení ({readAnomalies.Count}):");
            foreach (var a in readAnomalies.OrderBy(a => a.StartProgressPercent))
                sb.AppendLine($"   • {a.ToSummary()} (závažnost: {a.SeverityScore:F0}/100)");
            sb.AppendLine();
        }

        // Overlapping analysis
        var overlaps = FindOverlappingAnomalies(anomalies);
        if (overlaps.Count > 0)
        {
            sb.AppendLine($"🔄 Překrývající se anomálie (write+read): {overlaps.Count}");
            foreach (var (w, r) in overlaps)
            {
                var corr = ComputeCorrelationScore(w, r);
                var verdict = corr switch
                {
                    >= 70 => "🔴 Pravděpodobná fyzická vada",
                    >= 40 => "🟡 Možná slabá oblast",
                    _ => "🟢 Nízká korelace"
                };
                sb.AppendLine($"   • @{w.StartProgressPercent:F0}–{Math.Max(w.EndProgressPercent, r.EndProgressPercent):F0}%: " +
                              $"korelace {corr:F0}% — {verdict}");
            }
            sb.AppendLine();
        }

        // Repeating patterns
        var patterns = FindRepeatingPatterns(anomalies);
        if (patterns.Count > 0)
        {
            sb.AppendLine($"🔁 Opakující se vzory: {patterns.Count}");
            foreach (var group in patterns)
            {
                sb.AppendLine($"   • Pozice ~{group[0].StartProgressPercent:F0}%: " +
                              $"{group.Count} výskytů ({string.Join(", ", group.Select(a => a.Phase))})");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Computes an anomaly-based health penalty (0-50 points to subtract from score).
    /// </summary>
    public double ComputeAnomalyPenalty(List<SpeedAnomaly> anomalies)
    {
        if (anomalies.Count == 0) return 0;

        double penalty = 0;

        // Base penalty per anomaly
        penalty += anomalies.Count * 3;

        // Extra penalty for high-severity anomalies
        penalty += anomalies.Count(a => a.SeverityScore >= 70) * 5;
        penalty += anomalies.Count(a => a.SeverityScore >= 90) * 10;

        // Extra penalty for overlapping write+read anomalies (likely physical defect)
        var overlaps = FindOverlappingAnomalies(anomalies);
        penalty += overlaps.Count * 8;

        // Extra penalty for repeating patterns
        var patterns = FindRepeatingPatterns(anomalies);
        penalty += patterns.Sum(g => (g.Count - 1) * 5);

        return Math.Min(penalty, 50);
    }

    private static bool Overlaps(double start1, double end1, double start2, double end2)
    {
        return start1 <= end2 && start2 <= end1;
    }
}
