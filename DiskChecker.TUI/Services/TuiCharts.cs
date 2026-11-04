using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DiskChecker.TUI.Services;

/// <summary>Chart rendering style.</summary>
public enum TuiChartStyle
{
    /// <summary>Regular line chart with a connected trace.</summary>
    Line,

    /// <summary>Filled column chart (histograms, per-phase volume).</summary>
    Area,
}

/// <summary>A single chart data point.</summary>
public readonly record struct TuiPoint(double X, double Y);

/// <summary>A named series of chart points with its own colour.</summary>
public sealed class TuiSeries
{
    public TuiSeries(string name, Color color)
    {
        Name = name;
        Color = color;
    }

    public string Name { get; }

    public Color Color { get; }

    public List<TuiPoint> Points { get; } = new();

    public bool HasData => Points.Count > 0;

    public void Add(double x, double y) => Points.Add(new TuiPoint(x, y));

    public void AddRange(IEnumerable<TuiPoint> points) => Points.AddRange(points);
}

/// <summary>
/// Terminal chart description. Renderers turn this into coloured monospace rows;
/// the data model itself is UI agnostic so it can be unit tested.
/// </summary>
public sealed class TuiChart
{
    public List<TuiSeries> Series { get; } = new();

    public TuiChartStyle Style { get; set; } = TuiChartStyle.Line;

    public string? XLabel { get; set; }

    public string? YLabel { get; set; }

    public string YFormat { get; set; } = "F0";

    public string XFormat { get; set; } = "F0";

    public string XSuffix { get; set; } = string.Empty;

    public double? MinX { get; set; }

    public double? MaxX { get; set; }

    public double? MinY { get; set; }

    public double? MaxY { get; set; }

    public bool HasData => Series.Any(series => series.HasData);

    public TuiSeries AddSeries(string name, Color color)
    {
        var series = new TuiSeries(name, color);
        Series.Add(series);
        return series;
    }

    public void Clear()
    {
        foreach (var series in Series)
        {
            series.Points.Clear();
        }
    }

    public string[] BuildLines(int width, int height) =>
        TuiChartRenderer.BuildLines(this, width, height);

    public IRenderable Render(int width, int height) =>
        TuiChartRenderer.Render(this, width, height);
}

/// <summary>A coloured chart cell.</summary>
public readonly record struct TuiCell(char Text, Color? Color);

/// <summary>
/// Converts chart descriptions into rows of coloured characters.
/// The renderer deliberately produces plain text rows first so the mapping from
/// data to pixels can be asserted in unit tests without a terminal.
/// </summary>
public static class TuiChartRenderer
{
    private const int LabelWidth = 9;
    internal static readonly char[] Blocks = { '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

    public static string[] BuildLines(TuiChart chart, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var cells = BuildCells(chart, width, height);
        return cells.Select(row => new string(row.Select(cell => cell.Text).ToArray())).ToArray();
    }

    public static TuiCell[][] BuildCells(TuiChart chart, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(chart);
        width = Math.Clamp(width, 24, 400);
        height = Math.Clamp(height, 3, 60);

        int plotHeight = height - 1;
        int plotWidth = Math.Max(6, width - LabelWidth);

        var rows = new TuiCell[height][];
        for (int row = 0; row < height; row++)
        {
            rows[row] = new TuiCell[width];
            for (int column = 0; column < width; column++)
            {
                rows[row][column] = new TuiCell(' ', null);
            }
        }

        if (!chart.HasData)
        {
            WriteCenter(rows, 0, width, "...");
            return rows;
        }

        var (minX, maxX) = ResolveXBounds(chart);
        var (minY, maxY) = ResolveYBounds(chart);

        // Y axis labels: max, middle, min.
        WriteLabel(rows[0], maxY.ToString(chart.YFormat, CultureInfo.InvariantCulture));
        WriteLabel(rows[plotHeight / 2], ((maxY + minY) / 2).ToString(chart.YFormat, CultureInfo.InvariantCulture));
        WriteLabel(rows[plotHeight - 1], minY.ToString(chart.YFormat, CultureInfo.InvariantCulture));

        foreach (var series in chart.Series)
        {
            if (!series.HasData)
            {
                continue;
            }

            int? previousRow = null;
            for (int column = 0; column < plotWidth; column++)
            {
                double? value = ColumnValue(series, column, plotWidth, minX, maxX);
                if (value is null)
                {
                    continue;
                }

                double ratio = (value.Value - minY) / (maxY - minY);
                ratio = Math.Clamp(ratio, 0, 1);
                int rowFromTop = (int)Math.Round((1 - ratio) * (plotHeight - 1));
                rowFromTop = Math.Clamp(rowFromTop, 0, plotHeight - 1);
                int screenColumn = LabelWidth + column;

                if (chart.Style == TuiChartStyle.Area)
                {
                    for (int row = rowFromTop; row < plotHeight; row++)
                    {
                        rows[row][screenColumn] = new TuiCell('█', series.Color);
                    }
                }
                else
                {
                    if (previousRow is not null && previousRow != rowFromTop)
                    {
                        int from = Math.Min(previousRow.Value, rowFromTop);
                        int to = Math.Max(previousRow.Value, rowFromTop);
                        for (int row = from; row <= to; row++)
                        {
                            rows[row][screenColumn] = new TuiCell('│', series.Color);
                        }
                    }

                    rows[rowFromTop][screenColumn] = new TuiCell('█', series.Color);
                }

                previousRow = rowFromTop;
            }
        }

        // X axis labels.
        var axisRow = rows[height - 1];
        WriteText(axisRow, LabelWidth, FormatX(minX, chart));
        string middle = FormatX((minX + maxX) / 2, chart);
        WriteText(axisRow, LabelWidth + (plotWidth / 2) - (middle.Length / 2), middle);
        string last = FormatX(maxX, chart);
        WriteText(axisRow, LabelWidth + plotWidth - last.Length, last);

        return rows;
    }

    /// <summary>Builds a Spectre renderable for the chart (one markup line per row).</summary>
    public static IRenderable Render(TuiChart chart, int width, int height)
    {
        var cells = BuildCells(chart, width, height);
        var lines = new List<IRenderable>(cells.Length);
        foreach (var row in cells)
        {
            lines.Add(new Markup(BuildMarkup(row)));
        }

        return new Rows(lines);
    }

    /// <summary>Renders chart rows into Spectre markup, grouping equal colours into runs.</summary>
    public static string BuildMarkup(TuiCell[] row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var builder = new System.Text.StringBuilder();
        int index = 0;
        while (index < row.Length)
        {
            var color = row[index].Color;
            var run = new System.Text.StringBuilder();
            while (index < row.Length && Nullable.Equals(row[index].Color, color))
            {
                run.Append(row[index].Text);
                index++;
            }

            string text = Markup.Escape(run.ToString());
            builder.Append(color is null ? text : $"[{color.Value.ToMarkup()}]{text}[/]");
        }

        return builder.ToString();
    }

    private static (double Min, double Max) ResolveXBounds(TuiChart chart)
    {
        if (chart.MinX is not null && chart.MaxX is not null)
        {
            return (chart.MinX.Value, chart.MaxX.Value);
        }

        double min = double.MaxValue;
        double max = double.MinValue;
        foreach (var point in chart.Series.SelectMany(series => series.Points))
        {
            min = Math.Min(min, point.X);
            max = Math.Max(max, point.X);
        }

        if (min == double.MaxValue)
        {
            return (0, 100);
        }

        min = chart.MinX ?? min;
        max = chart.MaxX ?? max;
        if (max <= min)
        {
            max = min + 1;
        }

        return (min, max);
    }

    private static (double Min, double Max) ResolveYBounds(TuiChart chart)
    {
        double dataMax = 0;
        double dataMin = 0;
        bool any = false;
        foreach (var point in chart.Series.SelectMany(series => series.Points))
        {
            dataMax = any ? Math.Max(dataMax, point.Y) : point.Y;
            dataMin = any ? Math.Min(dataMin, point.Y) : point.Y;
            any = true;
        }

        double min = chart.MinY ?? Math.Min(0, dataMin);
        double max = chart.MaxY ?? (dataMax <= min ? min + 1 : dataMax * 1.1);
        if (max <= min)
        {
            max = min + 1;
        }

        return (min, max);
    }

    private static double? ColumnValue(TuiSeries series, int column, int plotWidth, double minX, double maxX)
    {
        double start = minX + (maxX - minX) * column / plotWidth;
        double end = minX + (maxX - minX) * (column + 1) / plotWidth;
        double sum = 0;
        int count = 0;
        double last = double.NaN;
        foreach (var point in series.Points)
        {
            if (point.X >= start && point.X < end)
            {
                sum += point.Y;
                count++;
                last = point.Y;
            }
        }

        if (count > 0)
        {
            return sum / count;
        }

        // The final column also claims the upper bound so a single point at maxX is drawn.
        if (column == plotWidth - 1)
        {
            foreach (var point in series.Points)
            {
                if (Math.Abs(point.X - maxX) < 1e-9)
                {
                    return point.Y;
                }
            }
        }

        return double.IsNaN(last) ? null : last;
    }

    private static string FormatX(double value, TuiChart chart) =>
        value.ToString(chart.XFormat, CultureInfo.InvariantCulture) + chart.XSuffix;

    private static void WriteLabel(TuiCell[] row, string text)
    {
        string padded = text.Length >= LabelWidth ? text[..(LabelWidth - 1)] : text;
        int start = LabelWidth - 1 - padded.Length;
        for (int i = 0; i < padded.Length; i++)
        {
            row[Math.Max(0, start + i)] = new TuiCell(padded[i], TuiTheme.Muted);
        }
    }

    private static void WriteText(TuiCell[] row, int start, string text)
    {
        for (int i = 0; i < text.Length && start + i < row.Length; i++)
        {
            if (start + i < 0)
            {
                continue;
            }

            row[start + i] = new TuiCell(text[i], TuiTheme.Muted);
        }
    }

    private static void WriteCenter(TuiCell[][] rows, int rowIndex, int width, string text)
    {
        int start = Math.Max(0, (width - text.Length) / 2);
        var row = rows[Math.Min(rowIndex, rows.Length - 1)];
        WriteText(row, start, text);
    }
}

/// <summary>Factory helpers building common chart shapes from raw data.</summary>
public static class TuiChartFactory
{
    /// <summary>Builds a line chart from (x, y) samples.</summary>
    public static TuiChart Line(
        IEnumerable<TuiPoint> points,
        string seriesName,
        Color color,
        string yFormat = "F1",
        string? xSuffix = null)
    {
        var chart = new TuiChart { YFormat = yFormat, XSuffix = xSuffix ?? string.Empty };
        chart.AddSeries(seriesName, color).AddRange(points);
        return chart;
    }

    /// <summary>Builds a histogram (area style) of the supplied values.</summary>
    public static TuiChart Histogram(IEnumerable<double> values, int buckets, Color color, string seriesName = "Vzorky")
    {
        var data = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        var chart = new TuiChart { Style = TuiChartStyle.Area, YFormat = "F0", XFormat = "F1" };
        var series = chart.AddSeries(seriesName, color);
        if (data.Count == 0 || buckets <= 0)
        {
            return chart;
        }

        double min = data.Min();
        double max = data.Max();
        if (max - min < 1e-9)
        {
            max = min + 1;
        }

        var counts = new int[buckets];
        foreach (double value in data)
        {
            int index = (int)((value - min) / (max - min) * buckets);
            index = Math.Clamp(index, 0, buckets - 1);
            counts[index]++;
        }

        double width = (max - min) / buckets;
        for (int i = 0; i < buckets; i++)
        {
            series.Add(min + (width * (i + 0.5)), counts[i]);
        }

        chart.MinX = min;
        chart.MaxX = max;
        chart.MinY = 0;
        return chart;
    }

    /// <summary>Single row block sparkline of the supplied samples.</summary>
    public static string Sparkline(IEnumerable<double> values, int maxWidth = 60)
    {
        var data = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        if (data.Count == 0)
        {
            return string.Empty;
        }

        if (data.Count > maxWidth)
        {
            int step = (int)Math.Ceiling(data.Count / (double)maxWidth);
            data = data.Where((_, index) => index % step == 0).ToList();
        }

        double max = data.Max();
        double min = data.Min();
        double range = max - min;
        var builder = new System.Text.StringBuilder(data.Count);
        foreach (double value in data)
        {
            double ratio = range < 1e-9 ? 1 : (value - min) / range;
            int index = Math.Clamp((int)Math.Round(ratio * (TuiChartRenderer.Blocks.Length - 1)), 0, TuiChartRenderer.Blocks.Length - 1);
            builder.Append(TuiChartRenderer.Blocks[index]);
        }

        return builder.ToString();
    }
}
