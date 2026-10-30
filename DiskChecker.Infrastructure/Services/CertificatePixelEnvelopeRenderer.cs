using DiskChecker.Core.Models;
using SkiaSharp;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// Experimental, bounded-memory PNG renderer for certificate throughput traces.
/// Accepts complete sample streams; no assumption about sample-count or spacing.
/// Not wired into certificate export until persisted sample streaming is available.
/// </summary>
public static class CertificatePixelEnvelopeRenderer
{
    public sealed class Columns
    {
        private readonly double[] _minimum;
        private readonly double[] _maximum;
        private readonly double[] _sum;
        private readonly int[] _count;
        private readonly bool[] _stall;
        private double _observedMaximum;

        public Columns(int width)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
            _minimum = new double[width];
            _maximum = new double[width];
            _sum = new double[width];
            _count = new int[width];
            _stall = new bool[width];
            Array.Fill(_minimum, double.PositiveInfinity);
        }

        public int Width => _count.Length;
        public int SampleCount { get; private set; }
        public double ObservedMaximum => _observedMaximum;
        public int GetCount(int column) => _count[column];
        public double GetMinimum(int column) => _minimum[column];
        public double GetMaximum(int column) => _maximum[column];
        public double GetAverage(int column) => _count[column] == 0 ? 0 : _sum[column] / _count[column];
        public bool HasStall(int column) => _stall[column];

        /// <summary>Consumes exactly one persisted measurement; memory remains O(width).</summary>
        public void Add(SpeedSample sample)
        {
            ArgumentNullException.ThrowIfNull(sample);
            if (!double.IsFinite(sample.ProgressPercent) || !double.IsFinite(sample.SpeedMBps))
                return;
            var progress = Math.Clamp(sample.ProgressPercent, 0d, 100d);
            var speed = Math.Max(0d, sample.SpeedMBps);
            int x = Math.Min(Width - 1, (int)(progress * (Width - 1) / 100d));
            _minimum[x] = Math.Min(_minimum[x], speed);
            _maximum[x] = Math.Max(_maximum[x], speed);
            _sum[x] += speed;
            _count[x]++;
            _stall[x] |= sample.IsStalled;
            _observedMaximum = Math.Max(_observedMaximum, speed);
            SampleCount++;
        }

        public void AddRange(IEnumerable<SpeedSample> samples)
        {
            ArgumentNullException.ThrowIfNull(samples);
            foreach (var sample in samples)
                Add(sample);
        }
    }

    /// <summary>
    /// Draws a detailed raster profile as PNG. Each horizontal pixel retains the
    /// minimum/maximum of all measurements mapping to that position. Empty columns
    /// remain empty; their gaps are not interpolated into invented readings.
    /// </summary>
    public static byte[] RenderPng(Columns write, Columns read, int height = 420)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 64);
        if (write.Width != read.Width)
            throw new ArgumentException("Write and read profiles must have matching widths.", nameof(read));

        const int left = 56, right = 16, top = 22, bottom = 36;
        int width = checked(write.Width + left + right);
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        double maxSpeed = Math.Max(1, Math.Max(write.ObservedMaximum, read.ObservedMaximum) * 1.08);
        float plotHeight = height - top - bottom;
        using var grid = new SKPaint { Color = new SKColor(227, 232, 239), StrokeWidth = 1, IsAntialias = false };
        using var text = new SKPaint { Color = new SKColor(65, 75, 90), IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, 12);
        for (int tick = 0; tick <= 4; tick++)
        {
            float y = top + plotHeight * tick / 4f;
            canvas.DrawLine(left, y, width - right, y, grid);
            canvas.DrawText(((4 - tick) * maxSpeed / 4).ToString("F0", System.Globalization.CultureInfo.InvariantCulture),
                4, y + 4, font, text);
        }

        DrawColumns(canvas, write, left, top, plotHeight, maxSpeed, new SKColor(201, 53, 58));
        DrawColumns(canvas, read, left, top, plotHeight, maxSpeed, new SKColor(22, 135, 91));
        canvas.DrawText("0 %", left, height - 10, font, text);
        canvas.DrawText("100 %", width - right - 38, height - 10, font, text);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawColumns(SKCanvas canvas, Columns columns, int left, int top,
        float plotHeight, double maxSpeed, SKColor color)
    {
        using var range = new SKPaint { Color = color, StrokeWidth = 1, IsAntialias = false };
        using var center = new SKPaint { Color = color.WithAlpha(220), StrokeWidth = 1, IsAntialias = false };
        using var stalled = new SKPaint { Color = SKColors.DarkOrange, StrokeWidth = 1, IsAntialias = false };
        for (int x = 0; x < columns.Width; x++)
        {
            if (columns.GetCount(x) == 0)
                continue;
            float yMin = top + plotHeight * (float)(1 - columns.GetMinimum(x) / maxSpeed);
            float yMax = top + plotHeight * (float)(1 - columns.GetMaximum(x) / maxSpeed);
            float yAvg = top + plotHeight * (float)(1 - columns.GetAverage(x) / maxSpeed);
            canvas.DrawLine(left + x, yMin, left + x, yMax, range);
            canvas.DrawPoint(left + x, yAvg, center);
            if (columns.HasStall(x))
                canvas.DrawLine(left + x, top + plotHeight - 5, left + x, top + plotHeight, stalled);
        }
    }
}
