using Spectre.Console;
using DiskChecker.TUI.Services;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class TuiChartsTests
{
    [Fact]
    public void EmptyChart_RendersPlaceholderWithoutThrowing()
    {
        var chart = new TuiChart();
        string[] lines = chart.BuildLines(40, 6);
        Assert.Equal(6, lines.Length);
        Assert.All(lines, line => Assert.Equal(40, line.Length));
        Assert.Contains("...", string.Join("\n", lines), StringComparison.Ordinal);
    }

    [Fact]
    public void LineChart_DrawsRisingSeries()
    {
        var chart = new TuiChart { MinX = 0, MaxX = 100, MinY = 0, MaxY = 10, YFormat = "F0" };
        var series = chart.AddSeries("write", TuiTheme.WriteSeries);
        for (int i = 0; i <= 100; i += 5)
        {
            series.Add(i, i / 10d);
        }

        string[] lines = chart.BuildLines(60, 10);
        Assert.Equal(10, lines.Length);

        // The lowest sample sits on the bottom row, the highest near the top row.
        string bottom = lines[8];
        Assert.Contains('█', bottom);
        Assert.Contains('█', string.Join(string.Empty, lines));

        // First sample (x = 0, y = 0) is drawn on the last plot row, right after the labels.
        Assert.Equal('█', lines[8][9]);

        // The trace reaches the top row on the right hand side.
        Assert.NotEqual(' ', lines[0][57]);
    }

    [Fact]
    public void LineChart_WritesAxisLabels()
    {
        var chart = new TuiChart { MinX = 0, MaxX = 100, MinY = 0, MaxY = 100, YFormat = "F0", XSuffix = "%" };
        chart.AddSeries("w", TuiTheme.WriteSeries).Add(50, 50);

        string joined = string.Join("\n", chart.BuildLines(70, 9));
        Assert.Contains("100", joined, StringComparison.Ordinal);
        Assert.Contains("0%", joined, StringComparison.Ordinal);
        Assert.Contains("100%", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoSeries_AreColouredIndependently()
    {
        var chart = new TuiChart { MinX = 0, MaxX = 10, MinY = 0, MaxY = 10 };
        chart.AddSeries("write", TuiTheme.WriteSeries).Add(2, 8);
        chart.AddSeries("read", TuiTheme.ReadSeries).Add(8, 2);

        var cells = TuiChartRenderer.BuildCells(chart, 40, 8);
        var colours = cells.SelectMany(row => row)
            .Where(cell => cell.Color is not null)
            .Select(cell => cell.Color!.Value)
            .Distinct()
            .ToList();

        Assert.Contains(TuiTheme.WriteSeries, colours);
        Assert.Contains(TuiTheme.ReadSeries, colours);
    }

    [Fact]
    public void Histogram_BucketsValues()
    {
        var values = Enumerable.Range(0, 100).Select(i => i * 0.5);
        var chart = TuiChartFactory.Histogram(values, 10, TuiTheme.Accent);
        Assert.True(chart.HasData);
        Assert.Equal(10, chart.Series[0].Points.Count);
        Assert.Equal(TuiChartStyle.Area, chart.Style);
        Assert.Equal(100d, chart.Series[0].Points.Sum(point => point.Y), 3);
    }

    [Fact]
    public void Histogram_UsesAreaStyleCells()
    {
        var chart = TuiChartFactory.Histogram(new[] { 1d, 1d, 1d, 5d }, 4, TuiTheme.Accent);
        string[] lines = chart.BuildLines(40, 6);
        Assert.Contains('█', string.Join(string.Empty, lines));
    }

    [Fact]
    public void Sparkline_UsesBlockRamp()
    {
        string spark = TuiChartFactory.Sparkline(new double[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        Assert.Equal(8, spark.Length);
        Assert.Equal('▁', spark[0]);
        Assert.Equal('█', spark[^1]);
    }

    [Fact]
    public void Sparkline_HandlesConstantValues()
    {
        string spark = TuiChartFactory.Sparkline(new[] { 5d, 5d, 5d });
        Assert.Equal("███", spark);
    }

    [Fact]
    public void Sparkline_DownsamplesLongSeries()
    {
        string spark = TuiChartFactory.Sparkline(Enumerable.Range(0, 1000).Select(i => (double)i), 50);
        Assert.True(spark.Length <= 50);
    }

    [Fact]
    public void Markup_RendersWithoutFormattingErrors()
    {
        var chart = new TuiChart { MinX = 0, MaxX = 100 };
        chart.AddSeries("a", TuiTheme.WriteSeries).Add(10, 10);
        var rendered = TuiIo.ToPlainText(chart.Render(60, 8), 60);
        Assert.Contains('█', rendered);
    }

    [Fact]
    public void BuildCells_ClampsExtremeGeometry()
    {
        var chart = TuiChartFactory.Histogram(new[] { 1d, 2d, 3d }, 3, TuiTheme.Accent);
        var cells = TuiChartRenderer.BuildCells(chart, 4, 1);
        Assert.True(cells.Length >= 3);
        Assert.All(cells, row => Assert.True(row.Length >= 24));
    }
}
