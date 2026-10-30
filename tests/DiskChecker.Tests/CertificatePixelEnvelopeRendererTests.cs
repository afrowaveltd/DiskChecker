using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Services;
using SkiaSharp;
using Xunit;

namespace DiskChecker.Tests;

public class CertificatePixelEnvelopeRendererTests
{
    [Fact]
    public void DenseSamples_PreserveLocalExtremaWithBoundedColumns()
    {
        var columns = new CertificatePixelEnvelopeRenderer.Columns(100);
        for (int i = 0; i < 100_000; i++)
        {
            columns.Add(new SpeedSample
            {
                ProgressPercent = 50,
                SpeedMBps = i == 345 ? 2 : i == 678 ? 180 : 100,
                IsStalled = i == 345
            });
        }

        Assert.Equal(100_000, columns.SampleCount);
        Assert.Equal(100_000, columns.GetCount(49));
        Assert.Equal(2, columns.GetMinimum(49));
        Assert.Equal(180, columns.GetMaximum(49));
        Assert.True(columns.HasStall(49));
        Assert.Equal(0, columns.GetCount(0));
    }

    [Fact]
    public void ProgressCoordinates_KeepFirstAndLastPixelAndGaps()
    {
        var columns = new CertificatePixelEnvelopeRenderer.Columns(101);
        columns.Add(new SpeedSample { ProgressPercent = 0, SpeedMBps = 25 });
        columns.Add(new SpeedSample { ProgressPercent = 100, SpeedMBps = 95 });

        Assert.Equal(1, columns.GetCount(0));
        Assert.Equal(1, columns.GetCount(100));
        Assert.Equal(0, columns.GetCount(50));
    }

    [Fact]
    public void RenderPng_CreatesReadableImageWithoutRawSampleHistory()
    {
        var write = new CertificatePixelEnvelopeRenderer.Columns(128);
        var read = new CertificatePixelEnvelopeRenderer.Columns(128);
        write.Add(new SpeedSample { ProgressPercent = 0, SpeedMBps = 100 });
        write.Add(new SpeedSample { ProgressPercent = 0, SpeedMBps = 75 });
        read.Add(new SpeedSample { ProgressPercent = 100, SpeedMBps = 90 });

        var png = CertificatePixelEnvelopeRenderer.RenderPng(write, read, 120);
        using var bitmap = SKBitmap.Decode(png);

        Assert.NotNull(bitmap);
        Assert.Equal(200, bitmap.Width);
        Assert.Equal(120, bitmap.Height);
    }
}
