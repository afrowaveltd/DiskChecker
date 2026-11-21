using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Projects measured test samples to the fixed certificate preview viewport.
/// Both certificate screens use the same mapping. Missing data stays missing:
/// synthetic three-point curves built from averages are never produced.
/// </summary>
public static class CertificateChartProjector
{
    public const int MaxSamplesPerPhase = 1024;
    private const double Left = 56;
    private const double Right = 886;
    private const double Top = 18;
    private const double Bottom = 226;

    /// <summary>
    /// Uses original progress coordinates if available and otherwise the bounded,
    /// historic certificate profile with positions inferred from its index.
    /// </summary>
    public static CertificateGraphData Build(
        DiskCertificate certificate,
        IReadOnlyList<SpeedSample>? write,
        IReadOnlyList<SpeedSample>? read,
        IReadOnlyList<TemperatureSample>? temperatures = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var writeData = Prepare(write, certificate.IsReadOnlyTest ? [] : certificate.WriteProfilePoints,
            certificate.IsReadOnlyTest);
        var readData = Prepare(read, certificate.ReadProfilePoints);
        if (writeData.Count == 0 && readData.Count == 0)
            return CertificateGraphData.Default;

        var valid = writeData.Concat(readData)
            .Select(s => s.SpeedMBps)
            .Where(double.IsFinite).ToArray();
        if (valid.Length == 0)
            return CertificateGraphData.Default;

        double minimum = Math.Min(0, valid.Min());
        double maximum = valid.Max();
        if (maximum <= minimum)
            maximum = minimum + 1;
        if (maximum < 1d)
            maximum = 1d;

        // Chart origin is real zero, not an arbitrary truncated baseline.
        // Zero-speed I/O stalls must remain visible.
        var temp = (temperatures ?? [])
            .Where(t => double.IsFinite(t.ProgressPercent))
            .OrderBy(t => t.ProgressPercent).Take(256).ToList();
        var hasTemp = temp.Count > 1;

        return new CertificateGraphData(
            Polyline(writeData, minimum, maximum),
            Polyline(readData, minimum, maximum),
            hasTemp ? TemperaturePolyline(temp) : "",
            hasTemp,
            $"{maximum:F1} MB/s",
            $"{(maximum / 2):F1} MB/s",
            "0 MB/s",
            "0 %",
            "50 %",
            "100 %");
    }

    private static List<SpeedSample> Prepare(
        IReadOnlyList<SpeedSample>? samples,
        IReadOnlyList<double>? historical,
        bool ignore = false)
    {
        if (ignore)
            return [];

        if (samples is { Count: > 0 })
        {
            // Preserve genuine stalls and measured progress instead of filtering
            // zero-speed points. Source SQL query already provides a bounded set.
            return Bound(samples
                .Where(s => double.IsFinite(s.SpeedMBps) && s.SpeedMBps >= 0
                    && double.IsFinite(s.ProgressPercent))
                .OrderBy(s => s.ProgressPercent).ToList(), MaxSamplesPerPhase);
        }

        if (historical is not { Count: > 0 })
            return [];

        var history = historical.Where(v => double.IsFinite(v) && v >= 0)
            .Take(MaxSamplesPerPhase).ToList();

        return history.Select((speed, i) => new SpeedSample
        {
            ProgressPercent = history.Count == 1 ? 50 : i * 100d / (history.Count - 1),
            SpeedMBps = speed
        }).ToList();
    }

    private static List<SpeedSample> Bound(IReadOnlyList<SpeedSample> samples, int limit)
    {
        if (samples.Count <= limit) return samples.ToList();

        // Include each bucket's first, minimum, maximum and last so short
        // stalls are not flattened into a healthy-looking average.
        int buckets = Math.Max(1, limit / 4);
        var output = new List<SpeedSample>(limit);
        for (int i = 0; i < buckets; i++)
        {
            int start = i * samples.Count / buckets;
            int end = Math.Min(samples.Count, (i + 1) * samples.Count / buckets);
            if (end <= start) continue;
            var part = samples.Skip(start).Take(end - start).ToList();
            output.Add(part[0]);
            output.Add(part.MinBy(s => s.SpeedMBps)!);
            output.Add(part.MaxBy(s => s.SpeedMBps)!);
            output.Add(part[^1]);
        }

        return output.OrderBy(s => s.ProgressPercent).ToList();
    }

    private static string Polyline(
        IReadOnlyList<SpeedSample> samples, double minimum, double maximum)
    {
        if (samples.Count == 0) return "";

        double range = Math.Max(0.0001, maximum - minimum);
        return string.Join(" ", samples.Select(s =>
        {
            double x = Left + Math.Clamp(s.ProgressPercent, 0, 100) / 100 * (Right - Left);
            double y = Bottom - Math.Clamp((s.SpeedMBps - minimum) / range, 0, 1) * (Bottom - Top);
            return FormattableString.Invariant($"{x:0.##},{y:0.##}");
        }));
    }

    private static string TemperaturePolyline(IReadOnlyList<TemperatureSample> samples)
    {
        double min = samples.Min(t => t.TemperatureCelsius);
        double max = samples.Max(t => t.TemperatureCelsius);
        double range = Math.Max(1, max - min);
        return string.Join(" ", samples.Select(s =>
        {
            double x = Left + Math.Clamp(s.ProgressPercent, 0, 100) / 100 * (Right - Left);
            double y = Bottom - Math.Clamp((s.TemperatureCelsius - min) / range, 0, 1) * (Bottom - Top);
            return FormattableString.Invariant($"{x:0.##},{y:0.##}");
        }));
    }
}
