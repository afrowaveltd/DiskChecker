using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// One consistent, bounded representation of seek latencies across certificate
/// screens. Every seek mode is a separate curve: mixing reset sample indices
/// produces a fictitious zigzag and destroys full-stroke signatures.
/// </summary>
public readonly record struct CertificateSeekGraphData(
    string FullStrokePoints,
    string RandomPoints,
    string SkipPoints,
    string LegacyPoints,
    string MaxLabel,
    string MidLabel,
    string MinLabel,
    string StartLabel,
    string MidIndexLabel,
    string EndLabel)
{
    public bool HasGraph => FullStrokePoints.Length > 0 ||
        RandomPoints.Length > 0 || SkipPoints.Length > 0 || LegacyPoints.Length > 0;

    public static CertificateSeekGraphData Empty { get; } = new(
        "", "", "", "", "N/A", "N/A", "0 ms", "1", "—", "—");
}

/// <summary>Projects original per-phase index/latency pairs onto the fixed preview plot.</summary>
public static class CertificateSeekChartProjector
{
    public const int MaxSamplesPerMode = 512;
    private const double Left = 56;
    private const double Right = 886;
    private const double Top = 18;
    private const double Bottom = 226;

    public static CertificateSeekGraphData Build(
        IEnumerable<SeekSampleRecord> samples,
        IReadOnlyList<double>? historical = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var valid = samples.Where(s => !s.HasError && s.Index > 0 &&
                double.IsFinite(s.LatencyMs) && s.LatencyMs > 0)
            .GroupBy(s => s.TestType)
            .ToDictionary(g => g.Key, g => Bound(g.OrderBy(s => s.Index).ToList()));

        // A legacy certificate contains only anonymous latency values, so the
        // mode is unknown. Treat these as one unlabeled full-stroke preview
        // only when no original per-mode samples survive.
        bool isLegacy = valid.Count == 0;
        if (isLegacy && historical is { Count: > 0 })
        {
            var values = historical.Where(s => double.IsFinite(s) && s > 0)
                .Select((ms, index) => new SeekSampleRecord
                {
                    Index = index + 1, LatencyMs = ms, TestType = SeekTestType.FullStroke
                }).ToList();
            if(values.Count > 0) valid[SeekTestType.FullStroke] = Bound(values);
        }

        if (valid.Count == 0)
            return CertificateSeekGraphData.Empty;

        var union = valid.Values.SelectMany(x => x).ToList();
        int lastIndex = union.Max(s => s.Index);
        var maxMeasured = union.Max(s => s.LatencyMs);
        double maxY = Math.Max(maxMeasured * 1.15, maxMeasured + 1);
        var last = lastIndex.ToString(CultureInfo.InvariantCulture);
        return new CertificateSeekGraphData(
            isLegacy ? "" : Points(valid, SeekTestType.FullStroke, lastIndex, maxY),
            Points(valid, SeekTestType.Random, lastIndex, maxY),
            Points(valid, SeekTestType.Skip, lastIndex, maxY),
            isLegacy ? Points(valid, SeekTestType.FullStroke, lastIndex, maxY) : "",
            $"{maxY:F1} ms",
            $"{maxY / 2:F1} ms",
            "0 ms",
            "1",
            Math.Max(1, lastIndex / 2).ToString(CultureInfo.InvariantCulture),
            last);
    }

    private static string Points(
        Dictionary<SeekTestType, List<SeekSampleRecord>> data,
        SeekTestType type, int lastIndex, double maxY)
    {
        if (!data.TryGetValue(type, out var samples))
            return string.Empty;
        return string.Join(" ", samples.Select(s =>
        {
            double x = Left + (s.Index - 1) / (double)Math.Max(1, lastIndex - 1) * (Right - Left);
            double y = Bottom - Math.Clamp(s.LatencyMs / maxY, 0, 1) * (Bottom - Top);
            return FormattableString.Invariant($"{x:0.##},{y:0.##}");
        }));
    }

    private static List<SeekSampleRecord> Bound(List<SeekSampleRecord> samples)
    {
        if (samples.Count <= MaxSamplesPerMode) return samples;
        const int perBucket = 4;
        int buckets = MaxSamplesPerMode / perBucket;
        var result = new List<SeekSampleRecord>(MaxSamplesPerMode);
        for (int i = 0; i < buckets; i++)
        {
            int first = (int)((long)i * samples.Count / buckets);
            int end = (int)((long)(i + 1) * samples.Count / buckets);
            if (end <= first) continue;
            var slice = samples.GetRange(first, end - first);
            result.Add(slice[0]);
            result.Add(slice.MinBy(s => s.LatencyMs)!);
            result.Add(slice.MaxBy(s => s.LatencyMs)!);
            result.Add(slice[^1]);
        }
        // Sorted real original indices prevent self-crossing paths.
        return result.OrderBy(s => s.Index).ToList();
    }
}
