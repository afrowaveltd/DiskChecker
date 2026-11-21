using System;
using System.Collections.Generic;
using System.Linq;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// An actual persisted seek operation, with its original (1-based) sample index.
/// Unlike ordinal chart points, this retains gaps after SQL downsampling.
/// </summary>
public readonly record struct SeekChartDatum(int SampleIndex, double LatencyMs);

/// <summary>
/// Builds the detail-card's OxyPlot latency chart from bounded, measured values.
/// Chart creation is independent from DB and can be regression-tested on its own.
/// </summary>
public static class DiskCardSeekChartBuilder
{
    /// <summary>Upper bound on plot markers to keep OxyPlot responsive.</summary>
    public const int MaxMarkers = 1024;

    /// <summary>
    /// Returns null when there are no valid measurements, rather than drawing
    /// a fabricated curve. Zero/invalid/error seek latencies are excluded.
    /// </summary>
    public static PlotModel? Build(
        IReadOnlyList<SeekChartDatum> data,
        string title,
        string latencyTitle,
        string sampleIndexTitle,
        string seriesTitle)
    {
        ArgumentNullException.ThrowIfNull(data);

        var valid = data.Where(p => p.SampleIndex > 0 &&
                double.IsFinite(p.LatencyMs) && p.LatencyMs > 0)
            .OrderBy(p => p.SampleIndex).ToList();

        if (valid.Count == 0)
            return null;

        var selected = KeepPeaks(valid, MaxMarkers);
        double maxLatency = valid.Max(p => p.LatencyMs);
        double yMax = Math.Max(maxLatency * 1.15, maxLatency + 1);
        int lastIndex = valid[^1].SampleIndex;

        var model = new PlotModel
        {
            Title = title,
            TitleFontSize = 13,
            TitleFontWeight = 700,
            PlotMargins = new OxyThickness(60, 10, 20, 40),
            Background = OxyColors.White
        };
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = latencyTitle,
            TitleFontSize = 11,
            Minimum = 0,
            Maximum = yMax,
            MajorStep = yMax > 10 ? Math.Ceiling(yMax / 5) : yMax / 5,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(30, 0, 0, 0),
            TickStyle = TickStyle.Outside,
            FontSize = 10
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = sampleIndexTitle,
            TitleFontSize = 11,
            Minimum = 1,
            Maximum = Math.Max(2, lastIndex),
            MajorStep = Math.Max(1, Math.Ceiling(lastIndex / 5d)),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromArgb(30, 0, 0, 0),
            TickStyle = TickStyle.Outside,
            FontSize = 10
        });

        var series = new ScatterSeries
        {
            Title = seriesTitle,
            MarkerType = MarkerType.Circle,
            MarkerSize = 2.4,
            MarkerFill = OxyColor.FromRgb(220, 38, 38),
            MarkerStroke = OxyColor.FromRgb(220, 38, 38),
            MarkerStrokeThickness = 0
        };
        foreach (var datum in selected)
            series.Points.Add(new ScatterPoint(datum.SampleIndex, datum.LatencyMs));
        model.Series.Add(series);
        return model;
    }

    private static IReadOnlyList<SeekChartDatum> KeepPeaks(
        IReadOnlyList<SeekChartDatum> valid, int limit)
    {
        if (valid.Count <= limit)
            return valid;

        int buckets = Math.Max(1, limit / 4);
        var output = new List<SeekChartDatum>(limit);
        for (int i = 0; i < buckets; i++)
        {
            int start = i * valid.Count / buckets;
            int end = Math.Min(valid.Count, (i + 1) * valid.Count / buckets);
            if (end <= start) continue;
            var range = valid.Skip(start).Take(end - start).ToList();
            output.Add(range[0]);
            output.Add(range.MinBy(p => p.LatencyMs));
            output.Add(range.MaxBy(p => p.LatencyMs));
            output.Add(range[^1]);
        }
        return output.OrderBy(p => p.SampleIndex).ToList();
    }
}
