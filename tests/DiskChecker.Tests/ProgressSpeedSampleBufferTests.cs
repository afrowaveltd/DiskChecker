using System;
using System.Linq;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Regressions for full-disk graphs of SSDs that stall and resume thousands
/// of times (e.g. a 512-GB device dropping from 340 MB/s to zero repeatedly).
/// </summary>
public class ProgressSpeedSampleBufferTests
{
    [Fact]
    public void MillionsOfChanges_CannotEvictTheFirstOrLastProgressPoint()
    {
        var buffer = new ProgressSpeedSampleBuffer(bucketCount: 2000);
        const int count = 100_000;

        for (int i = 0; i < count; i++)
            buffer.Add(Sample(i * 100d / (count - 1), i % 5 == 0 ? 0 : 340, i));

        Assert.InRange(buffer.Count, 2, buffer.MaxSamples);
        Assert.Equal(0, buffer.Samples[0].ProgressPercent);
        Assert.Equal(100, buffer.Samples[^1].ProgressPercent);
        Assert.Contains(buffer.Samples, s => s.SpeedMBps == 0 && s.ProgressPercent > 10);
        Assert.Contains(buffer.Samples, s => s.SpeedMBps == 340 && s.ProgressPercent > 80);
        Assert.True(buffer.Samples.Zip(buffer.Samples.Skip(1)).All(p =>
            p.First.Timestamp <= p.Second.Timestamp));
    }

    [Fact]
    public void EveryBucketRetainsTheFirstMinimumMaximumAndLastInOrder()
    {
        var buffer = new ProgressSpeedSampleBuffer(bucketCount: 10);
        buffer.Add(Sample(21, 200, 1));
        buffer.Add(Sample(22, 20, 2));
        buffer.Add(Sample(23, 350, 3));
        buffer.Add(Sample(24, 150, 4));
        buffer.Add(Sample(31, 180, 5));

        Assert.Equal(new[] { 200d, 20d, 350d, 150d, 180d },
            buffer.Samples.Select(s => s.SpeedMBps));
        Assert.Equal(4, buffer.ActiveStartIndex);
    }

    [Fact]
    public void StalledDeviceAtOneProgressPosition_UsesConstantMemory()
    {
        var buffer = new ProgressSpeedSampleBuffer(bucketCount: 2000);
        buffer.Add(Sample(0, 330, 0));
        for (int i = 1; i < 50_000; i++)
            buffer.Add(Sample(0, i % 2 == 0 ? 330 : 0, i));

        Assert.InRange(buffer.Count, 2, 4);
        Assert.Equal(0, buffer.Samples[0].ProgressPercent);
        Assert.Equal(49_999, (buffer.Samples[^1].Timestamp - Epoch).TotalMilliseconds);
        Assert.Contains(buffer.Samples, p => p.SpeedMBps == 0);
    }

    [Fact]
    public void TwoStageReduction_KeepsEarlyAndLateDipsWithinPersistenceBudget()
    {
        var live = new ProgressSpeedSampleBuffer(bucketCount: 2000);
        for (int i = 0; i <= 30_000; i++)
        {
            double pct = i / 300d;
            double speed = i % 19 == 0 ? 0 : (pct > 25 && pct < 30 ? 30 : 340);
            live.Add(Sample(pct, speed, i));
        }

        var saved = new ProgressSpeedSampleBuffer(bucketCount: 500);
        foreach (var s in live.Samples)
            saved.Add(s);

        Assert.True(saved.Count <= 2000);
        Assert.Equal(0, saved.Samples[0].ProgressPercent);
        Assert.Equal(100, saved.Samples[^1].ProgressPercent);
        Assert.Contains(saved.Samples, s => s.ProgressPercent is > 25 and < 30 && s.SpeedMBps == 30);
        Assert.Contains(saved.Samples, s => s.ProgressPercent > 75 && s.SpeedMBps == 0);
    }

    [Fact]
    public void Clear_ResetsProgressAndNoLongerRetainsPreviousTest()
    {
        var buffer = new ProgressSpeedSampleBuffer(bucketCount: 4);
        buffer.Add(Sample(0, 100, 0));
        buffer.Add(Sample(100, 200, 1));
        buffer.Clear();
        Assert.Empty(buffer.Samples);
        buffer.Add(Sample(0, 33, 2));
        Assert.Single(buffer.Samples);
        Assert.Equal(33, buffer.Samples[0].SpeedMBps);
    }

    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SpeedSample Sample(double percent, double speed, int millisecond) => new()
    {
        Timestamp = Epoch.AddMilliseconds(millisecond),
        ProgressPercent = percent,
        SpeedMBps = speed,
        Phase = "Write",
        IsStalled = speed == 0
    };
}
