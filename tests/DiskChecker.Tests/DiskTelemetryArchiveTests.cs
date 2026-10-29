using System;
using System.IO;
using System.Linq;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Xunit;

namespace DiskChecker.Tests;

public class DiskTelemetryArchiveTests
{
    [Fact]
    public void FiftyThousandSamples_UseSmallFileAndSupportPreciseZoom()
    {
        var sessionId = Guid.NewGuid();
        var path = DiskTelemetryArchive.GetSessionPath(sessionId);
        try
        {
            using (var archive = new DiskTelemetryArchive())
            {
                for (var i = 0; i < 50_000; i++)
                {
                    archive.Append(new SpeedSample
                    {
                        Timestamp = new DateTime(2026, 10, 29, 18, 0, 0, DateTimeKind.Utc)
                            .AddMilliseconds(i * 100L),
                        Elapsed = TimeSpan.FromMilliseconds(i * 100L),
                        ProgressPercent = i * 100d / 49_999d,
                        BytesProcessed = i * 1024L * 1024L,
                        SpeedMBps = i % 31 == 0 ? 0 : (i > 12_500 ? 30 : 340),
                        IsStalled = i % 31 == 0,
                        Phase = "Write"
                    });
                }
                archive.Complete(sessionId);
            }
            Assert.True(File.Exists(path));
            Assert.InRange(new FileInfo(path).Length, 1_000_000L, 5_000_000L);
            var overview = DiskTelemetryArchive.Read(sessionId, bucketsPerPhase: 500);
            Assert.InRange(overview.Count, 2, 2000);
            Assert.Equal(0, overview[0].ProgressPercent);
            Assert.Equal(100, overview[^1].ProgressPercent);
            Assert.Contains(overview, s => s.SpeedMBps == 0 && s.ProgressPercent > 40);
            var zoom = DiskTelemetryArchive.Read(sessionId, 24.9, 25.1, bucketsPerPhase: 200);
            Assert.NotEmpty(zoom);
            Assert.All(zoom, s => Assert.InRange(s.ProgressPercent, 24.9, 25.1));
            Assert.Contains(zoom, s => s.SpeedMBps == 30);
            Assert.All(zoom, s => Assert.Equal(TelemetrySamplePhase.Write, s.Phase));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ShortUnfilledBlocksAndBothPhasesSurviveArchiveCompletion()
    {
        var id = Guid.NewGuid();
        var path = DiskTelemetryArchive.GetSessionPath(id);
        try
        {
            using (var archive = new DiskTelemetryArchive())
            {
                for (var i = 0; i < 7; i++)
                {
                    archive.Append(new SpeedSample
                    {
                        Timestamp = DateTime.UtcNow.AddMilliseconds(i),
                        ProgressPercent = i * 10d,
                        SpeedMBps = 200 + i,
                        Phase = "Write"
                    });
                }
                for (var i = 0; i < 9; i++)
                {
                    archive.Append(new SpeedSample
                    {
                        Timestamp = DateTime.UtcNow.AddMilliseconds(i),
                        ProgressPercent = i * 10d,
                        SpeedMBps = i == 4 ? 0 : 400 + i,
                        IsStalled = i == 4,
                        Phase = "Read"
                    });
                }
                archive.Complete(id);
            }
            var samples = DiskTelemetryArchive.Read(id);
            Assert.Equal(16, samples.Count);
            Assert.Equal(7, samples.Count(s => s.Phase == TelemetrySamplePhase.Write));
            Assert.Equal(9, samples.Count(s => s.Phase == TelemetrySamplePhase.Read));
            Assert.Contains(samples, s => s.IsStalled && s.Phase == TelemetrySamplePhase.Read);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void StaleProgressCallbacks_NeverMoveRetainedGraphToTheLeft()
    {
        var buffer = new ProgressSpeedSampleBuffer(100);
        foreach (var x in new[] { 0d, 3d, 5d, 4.9d, 7d, 6d, 10d })
        {
            buffer.Add(new SpeedSample { ProgressPercent = x, SpeedMBps = 200 + x });
        }

        Assert.True(buffer.Samples.Zip(buffer.Samples.Skip(1))
            .All(pair => pair.First.ProgressPercent <= pair.Second.ProgressPercent));
        Assert.Equal(0, buffer.Samples[0].ProgressPercent);
        Assert.Equal(10, buffer.Samples[^1].ProgressPercent);
    }
}
