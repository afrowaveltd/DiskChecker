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
        // Temperature is a separate °C chart, NEVER a dimensionless overlay on
        // the speed MB/s axis. A seek-only test may have temperature without
        // a speed profile; in that case its temperature chart still renders.
        var temp = (temperatures ?? [])
            .Where(t => double.IsFinite(t.ProgressPercent) &&
                t.TemperatureCelsius > -100 && t.TemperatureCelsius < 200)
            .OrderBy(t => t.ProgressPercent).ToList();
        if (temp.Count > 256)
        {
            int count = temp.Count;
            temp = Enumerable.Range(0, 256)
                .Select(i => temp[(int)Math.Round(i * (count - 1) / 255d)]).ToList();
        }

        bool hasSpeed = writeData.Count > 0 || readData.Count > 0;
        bool hasTemp = temp.Count >= 2;
        if (!hasSpeed && !hasTemp)
            return CertificateGraphData.Default;

        double minSpeed = 0;
        double maxSpeed = 1;
        if (hasSpeed)
        {
            maxSpeed = Math.Max(1d, writeData.Concat(readData).Max(s => s.SpeedMBps));
        }

        int minTemp = hasTemp ? temp.Min(t => t.TemperatureCelsius) : 0;
        int maxTemp = hasTemp ? temp.Max(t => t.TemperatureCelsius) : 0;
        double temperatureBottom = minTemp - 2;
        double temperatureTop = Math.Max(temperatureBottom + 1, maxTemp + 2);

        return new CertificateGraphData(
            Polyline(writeData, minSpeed, maxSpeed),
            Polyline(readData, minSpeed, maxSpeed),
            hasTemp ? TemperaturePolyline(temp, temperatureBottom, temperatureTop) : "",
            hasTemp,
            hasSpeed ? $"{maxSpeed:F1} MB/s" : "N/A",
            hasSpeed ? $"{maxSpeed / 2:F1} MB/s" : "N/A",
            "0 MB/s",
            "0 %",
            "50 %",
            "100 %",
            hasTemp ? $"{temperatureTop:F0} °C" : "N/A",
            hasTemp ? $"{(temperatureTop + temperatureBottom) / 2:F0} °C" : "N/A",
            hasTemp ? $"{temperatureBottom:F0} °C" : "N/A");
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

    private static string TemperaturePolyline(
        IReadOnlyList<TemperatureSample> samples, double min, double max)
    {
        double range = Math.Max(1, max - min);
        return string.Join(" ", samples.Select(s =>
        {
            double x = Left + Math.Clamp(s.ProgressPercent, 0, 100) / 100 * (Right - Left);
            double y = Bottom - Math.Clamp((s.TemperatureCelsius - min) / range, 0, 1) * (Bottom - Top);
            return FormattableString.Invariant($"{x:0.##},{y:0.##}");
        }));
    }
}
