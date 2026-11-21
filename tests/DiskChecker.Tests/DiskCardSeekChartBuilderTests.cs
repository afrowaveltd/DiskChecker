using System;
using System.Linq;
using DiskChecker.UI.Avalonia.ViewModels;
using OxyPlot.Axes;
using OxyPlot.Series;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// No real hardware/database access. Checks that historical seek plots remain
/// bounded, retain real positions, and do not invent missing measurements.
/// </summary>
public sealed class DiskCardSeekChartBuilderTests
{
    private static OxyPlot.PlotModel? Build(params SeekChartDatum[] values) =>
        DiskCardSeekChartBuilder.Build(values, "Seek", "Latency ms", "Sample", "Measured");

    [Fact]
    public void MissingOrInvalidMeasurements_DoNotInventSeekGraph()
    {
        Assert.Null(Build());
        Assert.Null(Build(
            new SeekChartDatum(1, 0),
            new SeekChartDatum(2, double.NaN),
            new SeekChartDatum(3, double.PositiveInfinity)));
    }

    [Fact]
    public void DownsampledPlot_RetainsOriginalSampleIndicesAndLatencyScale()
    {
        var chart = Build(
            new SeekChartDatum(1, 5),
            new SeekChartDatum(80, 18),
            new SeekChartDatum(500, 92));

        Assert.NotNull(chart);
        var scatter = Assert.IsType<ScatterSeries>(Assert.Single(chart.Series));
        Assert.Equal(3, scatter.Points.Count);
        Assert.Equal(1, scatter.Points[0].X);
        Assert.Equal(80, scatter.Points[1].X);
        Assert.Equal(500, scatter.Points[2].X);
        Assert.Equal(92, scatter.Points[2].Y);
        var latencyAxis = Assert.IsType<LinearAxis>(
            chart.Axes.Single(a => a.Position == AxisPosition.Left));
        Assert.Equal(0, latencyAxis.Minimum);
    }

    [Fact]
    public void ManySeekSamples_PreserveWorstLatencyAndBoundMarkerCount()
    {
        var samples = Enumerable.Range(1, 40000).Select(i =>
            new SeekChartDatum(i, i == 20003 ? 998 : 15d)).ToArray();

        var chart = DiskCardSeekChartBuilder.Build(
            samples, "Seek", "Latency ms", "Sample", "Measured");

        Assert.NotNull(chart);
        var scatter = Assert.IsType<ScatterSeries>(Assert.Single(chart.Series));
        Assert.InRange(scatter.Points.Count, 1, DiskCardSeekChartBuilder.MaxMarkers);
        Assert.Contains(scatter.Points, p => p.X == 20003 && p.Y == 998);
        Assert.Equal(40000, scatter.Points.Max(p => p.X));
    }
}
