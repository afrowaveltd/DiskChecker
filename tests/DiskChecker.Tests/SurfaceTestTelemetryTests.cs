using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.UI.Avalonia.ViewModels;
using LiveChartsCore.Defaults;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Tests verifying that raw telemetry is stored independently of the live graph buffer,
/// that downsampling preserves critical information, and that the report/certificate
/// uses the full raw data series.
/// </summary>
public class SurfaceTestTelemetryTests
{
    // ── Reflection helpers ──────────────────────────────────────────

    private static List<SpeedSample> GetRawWriteSamples(SurfaceTestViewModel vm)
    {
        var field = typeof(SurfaceTestViewModel).GetField("_rawWriteSamples",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (List<SpeedSample>)field!.GetValue(vm)!;
    }

    private static List<SpeedSample> GetRawReadSamples(SurfaceTestViewModel vm)
    {
        var field = typeof(SurfaceTestViewModel).GetField("_rawReadSamples",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (List<SpeedSample>)field!.GetValue(vm)!;
    }

    private static int GetMaxGraphPoints()
    {
        var field = typeof(SurfaceTestViewModel).GetField("MaxGraphPoints",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (int)field!.GetValue(null)!;
    }

    private static void InvokeAddSpeedPointCore(SurfaceTestViewModel vm, double speed, double dataPercent, int phase)
    {
        var method = typeof(SurfaceTestViewModel).GetMethod("AddSpeedPointCore",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method!.Invoke(vm, new object?[] { speed, dataPercent, phase, (TimeSpan?)null });
    }

    private static void InvokeResetTestState(SurfaceTestViewModel vm)
    {
        var method = typeof(SurfaceTestViewModel).GetMethod("ResetTestState",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        method!.Invoke(vm, null);
    }

    // ── Helper: create a minimal ViewModel ───────────────────────────

    /// <summary>
    /// Creates a SurfaceTestViewModel with all dependencies null.
    /// Only graph collections and raw sample lists are usable.
    /// </summary>
    private static SurfaceTestViewModel CreateMinimalViewModel()
    {
        // Use the parameterless constructor path — the VM has only one constructor
        // with many DI parameters. We pass null for all; only graph-related
        // fields are initialized in the constructor body.
        var ctor = typeof(SurfaceTestViewModel).GetConstructors(BindingFlags.Public | BindingFlags.Instance)[0];
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length]; // all null
        return (SurfaceTestViewModel)ctor.Invoke(args);
    }

    // ── Test 1: Raw samples are not capped ───────────────────────────

    [Fact]
    public void RawSamples_AreNotCapped_After100kInserts()
    {
        var vm = CreateMinimalViewModel();

        const int sampleCount = 100_000;
        for (int i = 0; i < sampleCount; i++)
        {
            InvokeAddSpeedPointCore(vm, speed: 100.0 + (i % 50), dataPercent: i / 1000.0, phase: 0);
        }

        var rawWrites = GetRawWriteSamples(vm);
        Assert.Equal(sampleCount, rawWrites.Count);

        // Also verify read samples work the same way
        for (int i = 0; i < sampleCount; i++)
        {
            InvokeAddSpeedPointCore(vm, speed: 80.0 + (i % 30), dataPercent: i / 1000.0, phase: 1);
        }

        var rawReads = GetRawReadSamples(vm);
        Assert.Equal(sampleCount, rawReads.Count);
    }

    // ── Test 2: Live graph is capped at MaxGraphPoints ───────────────

    [Fact]
    public void LiveGraph_IsCapped_AtMaxGraphPoints()
    {
        var vm = CreateMinimalViewModel();
        var maxPoints = GetMaxGraphPoints();
        Assert.True(maxPoints > 0, "MaxGraphPoints should be > 0");

        // Insert more than MaxGraphPoints
        const int totalSamples = 100_000;
        for (int i = 0; i < totalSamples; i++)
        {
            InvokeAddSpeedPointCore(vm, speed: 100.0, dataPercent: i / 1000.0, phase: 0);
        }

        // Live graph (WriteSeriesValues) must not exceed MaxGraphPoints
        Assert.True(vm.WriteSeriesValues.Count <= maxPoints,
            $"WriteSeriesValues.Count={vm.WriteSeriesValues.Count} exceeds MaxGraphPoints={maxPoints}");

        // Raw samples must hold everything
        var rawWrites = GetRawWriteSamples(vm);
        Assert.Equal(totalSamples, rawWrites.Count);

        // Same for read
        for (int i = 0; i < totalSamples; i++)
        {
            InvokeAddSpeedPointCore(vm, speed: 80.0, dataPercent: i / 1000.0, phase: 1);
        }

        Assert.True(vm.ReadSeriesValues.Count <= maxPoints,
            $"ReadSeriesValues.Count={vm.ReadSeriesValues.Count} exceeds MaxGraphPoints={maxPoints}");

        var rawReads = GetRawReadSamples(vm);
        Assert.Equal(totalSamples, rawReads.Count);
    }

    // ── Test 3: Downsampling preserves first and last sample ─────────

    [Fact]
    public void DownsampleWithBuckets_PreservesFirstAndLastSample()
    {
        var rawSamples = new List<SpeedSample>();
        for (int i = 0; i < 10_000; i++)
        {
            rawSamples.Add(new SpeedSample
            {
                Timestamp = new DateTime(2025, 1, 1).AddMilliseconds(i * 100),
                SpeedMBps = 100.0 + (i % 20),
                ProgressPercent = i / 100.0,
                BytesProcessed = i * 1_000_000L,
                Phase = "Write"
            });
        }

        const int targetCount = 100;
        var result = SurfaceTestViewModel.DownsampleWithBuckets(rawSamples, targetCount);

        Assert.True(result.Count <= targetCount);
        Assert.Equal(rawSamples[0].Timestamp, result[0].Timestamp);
        Assert.Equal(rawSamples[0].SpeedMBps, result[0].SpeedMBps);
        Assert.Equal(rawSamples[0].ProgressPercent, result[0].ProgressPercent);

        Assert.Equal(rawSamples[^1].Timestamp, result[^1].Timestamp);
        Assert.Equal(rawSamples[^1].SpeedMBps, result[^1].SpeedMBps);
        Assert.Equal(rawSamples[^1].ProgressPercent, result[^1].ProgressPercent);
    }

    [Fact]
    public void DownsampleWithBuckets_SmallInput_ReturnsAllSamples()
    {
        var rawSamples = new List<SpeedSample>
        {
            new() { Timestamp = DateTime.UtcNow, SpeedMBps = 100, ProgressPercent = 0, Phase = "Write" },
            new() { Timestamp = DateTime.UtcNow, SpeedMBps = 120, ProgressPercent = 50, Phase = "Write" },
            new() { Timestamp = DateTime.UtcNow, SpeedMBps = 110, ProgressPercent = 100, Phase = "Write" }
        };

        var result = SurfaceTestViewModel.DownsampleWithBuckets(rawSamples, targetCount: 2000);

        // When input is smaller than target, all samples are returned
        Assert.Equal(3, result.Count);
    }

    // ── Test 4: Downsampling preserves stall sample ──────────────────

    [Fact]
    public void DownsampleWithBuckets_PreservesStallSample()
    {
        var rawSamples = new List<SpeedSample>();
        for (int i = 0; i < 5_000; i++)
        {
            rawSamples.Add(new SpeedSample
            {
                Timestamp = new DateTime(2025, 1, 1).AddMilliseconds(i * 100),
                SpeedMBps = 100.0,
                ProgressPercent = i / 50.0,
                BytesProcessed = i * 1_000_000L,
                Phase = "Write",
                IsStalled = false
            });
        }

        // Insert a stall at position 2500
        rawSamples[2500] = new SpeedSample
        {
            Timestamp = new DateTime(2025, 1, 1).AddMilliseconds(2500 * 100),
            SpeedMBps = 0,
            ProgressPercent = 50.0,
            BytesProcessed = 2500 * 1_000_000L,
            Phase = "Write",
            IsStalled = true
        };

        const int targetCount = 200;
        var result = SurfaceTestViewModel.DownsampleWithBuckets(rawSamples, targetCount);

        // At least one output sample must have IsStalled = true
        Assert.Contains(result, s => s.IsStalled);
    }

    [Fact]
    public void DownsampleWithBuckets_MultipleStalls_AllPreserved()
    {
        var rawSamples = new List<SpeedSample>();
        for (int i = 0; i < 10_000; i++)
        {
            rawSamples.Add(new SpeedSample
            {
                Timestamp = new DateTime(2025, 1, 1).AddMilliseconds(i * 100),
                SpeedMBps = 100.0,
                ProgressPercent = i / 100.0,
                BytesProcessed = i * 1_000_000L,
                Phase = "Write",
                IsStalled = false
            });
        }

        // Insert stalls at multiple positions
        rawSamples[1000] = new SpeedSample
        {
            Timestamp = rawSamples[1000].Timestamp,
            SpeedMBps = 0,
            ProgressPercent = rawSamples[1000].ProgressPercent,
            BytesProcessed = rawSamples[1000].BytesProcessed,
            Phase = rawSamples[1000].Phase,
            IsStalled = true
        };
        rawSamples[3000] = new SpeedSample
        {
            Timestamp = rawSamples[3000].Timestamp,
            SpeedMBps = 0,
            ProgressPercent = rawSamples[3000].ProgressPercent,
            BytesProcessed = rawSamples[3000].BytesProcessed,
            Phase = rawSamples[3000].Phase,
            IsStalled = true
        };
        rawSamples[7000] = new SpeedSample
        {
            Timestamp = rawSamples[7000].Timestamp,
            SpeedMBps = 0,
            ProgressPercent = rawSamples[7000].ProgressPercent,
            BytesProcessed = rawSamples[7000].BytesProcessed,
            Phase = rawSamples[7000].Phase,
            IsStalled = true
        };

        const int targetCount = 500;
        var result = SurfaceTestViewModel.DownsampleWithBuckets(rawSamples, targetCount);

        var stallCount = result.Count(s => s.IsStalled);
        Assert.True(stallCount >= 3,
            $"Expected at least 3 stall samples in output, got {stallCount}");
    }

    // ── Test 5: Downsampling preserves min/max/avg semantics ─────────

    [Fact]
    public void DownsampleWithBuckets_PreservesExtremeValues()
    {
        // Create samples where one bucket has a clear extreme
        var rawSamples = new List<SpeedSample>();
        for (int i = 0; i < 1_000; i++)
        {
            var speed = 100.0; // baseline
            // Bucket around i=500 gets an extreme spike
            if (i >= 480 && i <= 520)
            {
                speed = i == 500 ? 500.0 : 100.0 + Math.Abs(i - 500) * 10;
            }

            rawSamples.Add(new SpeedSample
            {
                Timestamp = new DateTime(2025, 1, 1).AddMilliseconds(i * 100),
                SpeedMBps = speed,
                ProgressPercent = i / 10.0,
                BytesProcessed = i * 1_000_000L,
                Phase = "Write"
            });
        }

        const int targetCount = 50;
        var result = SurfaceTestViewModel.DownsampleWithBuckets(rawSamples, targetCount);

        // The bucket containing the spike should have an average > baseline
        var maxAvgSpeed = result.Max(s => s.SpeedMBps);
        Assert.True(maxAvgSpeed > 110,
            $"Expected max average speed > 110 (spike preserved), got {maxAvgSpeed:F1}");

        // The overall min should be at or near baseline
        var minSpeed = result.Min(s => s.SpeedMBps);
        Assert.True(minSpeed >= 90,
            $"Expected min speed near baseline (>=90), got {minSpeed:F1}");

        // Verify the output is not just a flat average — there should be variation
        var distinctSpeeds = result.Select(s => s.SpeedMBps).Distinct().Count();
        Assert.True(distinctSpeeds > 3,
            $"Expected speed variation across buckets, got only {distinctSpeeds} distinct values");
    }

    [Fact]
    public void DownsampleWithBuckets_ExtremeNotFlattenedToPlainAverage()
    {
        // Create a scenario where one bucket has a massive outlier
        var rawSamples = new List<SpeedSample>();
        for (int i = 0; i < 5_000; i++)
        {
            var speed = i switch
            {
                >= 2000 and < 2100 => 10.0,   // severe dip
                _ => 100.0                      // baseline
            };

            rawSamples.Add(new SpeedSample
            {
                Timestamp = new DateTime(2025, 1, 1).AddMilliseconds(i * 100),
                SpeedMBps = speed,
                ProgressPercent = i / 50.0,
                BytesProcessed = i * 1_000_000L,
                Phase = "Write"
            });
        }

        const int targetCount = 100;
        var result = SurfaceTestViewModel.DownsampleWithBuckets(rawSamples, targetCount);

        // The bucket containing the dip should have a low average
        var minAvgSpeed = result.Min(s => s.SpeedMBps);
        Assert.True(minAvgSpeed < 90,
            $"Expected min average speed < 90 (dip preserved), got {minAvgSpeed:F1}");

        // Most buckets should be near baseline
        var medianSpeed = result.OrderBy(s => s.SpeedMBps).ElementAt(result.Count / 2).SpeedMBps;
        Assert.True(medianSpeed > 95,
            $"Expected median speed near baseline (>95), got {medianSpeed:F1}");
    }

    // ── Test 6: Regression — report/certificate uses raw samples ─────

    [Fact]
    public void PrepareSpeedSamplesForPersistence_UsesRetentionService_ForLargeInput()
    {
        // Create 20,000 samples (above the 15,000 safety limit)
        var samples = new List<SpeedSample>();
        for (int i = 0; i < 20_000; i++)
        {
            samples.Add(new SpeedSample
            {
                Timestamp = new DateTime(2025, 1, 1).AddMilliseconds(i * 100),
                SpeedMBps = 100.0 + (i % 30),
                ProgressPercent = i / 200.0,
                BytesProcessed = i * 1_000_000L,
                Phase = "Write"
            });
        }

        var result = DiskCardTestService.PrepareSpeedSamplesForPersistence(samples);

        // The safety net (SpeedSampleRetentionService) should have kicked in
        // Research profile max is 15,000
        Assert.True(result.Count <= 15_000,
            $"Expected <= 15,000 samples after retention, got {result.Count}");

        // First and last should be preserved
        Assert.Equal(samples[0].Timestamp, result[0].Timestamp);
        Assert.Equal(samples[^1].Timestamp, result[^1].Timestamp);
    }

    [Fact]
    public void PrepareSpeedSamplesForPersistence_SmallInput_PassesThrough()
    {
        var samples = new List<SpeedSample>();
        for (int i = 0; i < 500; i++)
        {
            samples.Add(new SpeedSample
            {
                Timestamp = new DateTime(2025, 1, 1).AddMilliseconds(i * 100),
                SpeedMBps = 100.0,
                ProgressPercent = i / 5.0,
                BytesProcessed = i * 1_000_000L,
                Phase = "Write"
            });
        }

        var result = DiskCardTestService.PrepareSpeedSamplesForPersistence(samples);

        // Small input should pass through (no retention service needed)
        Assert.True(result.Count > 0);
        Assert.True(result.Count <= samples.Count);
    }

    [Fact]
    public void PrepareSpeedSamplesForPersistence_TrimsLeadingZeros()
    {
        var samples = new List<SpeedSample>
        {
            new() { Timestamp = DateTime.UtcNow, SpeedMBps = 0, ProgressPercent = 0, Phase = "Write" },
            new() { Timestamp = DateTime.UtcNow, SpeedMBps = 0, ProgressPercent = 1, Phase = "Write" },
            new() { Timestamp = DateTime.UtcNow, SpeedMBps = 0, ProgressPercent = 2, Phase = "Write" },
            new() { Timestamp = DateTime.UtcNow, SpeedMBps = 50, ProgressPercent = 3, Phase = "Write" },
            new() { Timestamp = DateTime.UtcNow, SpeedMBps = 100, ProgressPercent = 4, Phase = "Write" },
            new() { Timestamp = DateTime.UtcNow, SpeedMBps = 0, ProgressPercent = 5, Phase = "Write" }, // zero after start — keep
        };

        var result = DiskCardTestService.PrepareSpeedSamplesForPersistence(samples);

        // Leading zeros should be trimmed, but zero after first non-zero is kept
        Assert.True(result.Count >= 3, $"Expected at least 3 samples, got {result.Count}");
        Assert.True(result[0].SpeedMBps > 0, "First sample should have non-zero speed");
    }

    // ── Integration: raw samples survive reset and are used for save ─

    [Fact]
    public void RawSamples_SurviveMultiplePhases()
    {
        var vm = CreateMinimalViewModel();

        // Write phase: 500 samples
        for (int i = 0; i < 500; i++)
        {
            InvokeAddSpeedPointCore(vm, speed: 100.0, dataPercent: i / 5.0, phase: 0);
        }

        // Read phase: 500 samples
        for (int i = 0; i < 500; i++)
        {
            InvokeAddSpeedPointCore(vm, speed: 80.0, dataPercent: i / 5.0, phase: 1);
        }

        var rawWrites = GetRawWriteSamples(vm);
        var rawReads = GetRawReadSamples(vm);

        Assert.Equal(500, rawWrites.Count);
        Assert.Equal(500, rawReads.Count);

        // All write samples should have Phase = "Write"
        Assert.All(rawWrites, s => Assert.Equal("Write", s.Phase));

        // All read samples should have Phase = "Read"
        Assert.All(rawReads, s => Assert.Equal("Read", s.Phase));
    }

    [Fact]
    public void RawSamples_AreIndependentOfLiveGraph()
    {
        var vm = CreateMinimalViewModel();
        var maxPoints = GetMaxGraphPoints();

        // Insert more than MaxGraphPoints
        const int totalSamples = 80_000;
        for (int i = 0; i < totalSamples; i++)
        {
            InvokeAddSpeedPointCore(vm, speed: 100.0 + (i % 20), dataPercent: i / 800.0, phase: 0);
        }

        // Live graph is capped
        Assert.True(vm.WriteSeriesValues.Count <= maxPoints);

        // Raw samples hold everything
        var rawWrites = GetRawWriteSamples(vm);
        Assert.Equal(totalSamples, rawWrites.Count);

        // Verify raw samples contain data that was trimmed from live graph
        // The first raw sample should have ProgressPercent = 0
        Assert.Equal(0, rawWrites[0].ProgressPercent);

        // Raw samples hold the full history; live graph is a subset
        // The live graph may keep the first point (downsampling anchor),
        // but raw samples always have more data than the live graph
        Assert.True(rawWrites.Count > vm.WriteSeriesValues.Count,
            $"Raw samples ({rawWrites.Count}) should exceed live graph ({vm.WriteSeriesValues.Count})");
    }

    // ── Edge cases ───────────────────────────────────────────────────

    [Fact]
    public void DownsampleWithBuckets_EmptyInput_ReturnsEmpty()
    {
        var result = SurfaceTestViewModel.DownsampleWithBuckets(new List<SpeedSample>(), 100);
        Assert.Empty(result);
    }

    [Fact]
    public void DownsampleWithBuckets_SingleSample_ReturnsSingleSample()
    {
        var samples = new List<SpeedSample>
        {
            new() { Timestamp = DateTime.UtcNow, SpeedMBps = 100, ProgressPercent = 50, Phase = "Write" }
        };

        var result = SurfaceTestViewModel.DownsampleWithBuckets(samples, 100);
        Assert.Single(result);
        Assert.Equal(100, result[0].SpeedMBps);
    }

    [Fact]
    public void DownsampleWithBuckets_TargetCountOne_ReturnsFirstAndLast()
    {
        var samples = new List<SpeedSample>();
        for (int i = 0; i < 100; i++)
        {
            samples.Add(new SpeedSample
            {
                Timestamp = new DateTime(2025, 1, 1).AddMilliseconds(i * 100),
                SpeedMBps = 100.0 + i,
                ProgressPercent = i,
                Phase = "Write"
            });
        }

        // targetCount = 1 is an edge case — the algorithm creates 1 bucket,
        // then first/last overwrite the same element (last wins)
        var result = SurfaceTestViewModel.DownsampleWithBuckets(samples, 1);

        // With 1 bucket, first and last are the same element — last overwrites first
        Assert.Single(result);
        Assert.Equal(samples[^1].SpeedMBps, result[0].SpeedMBps);
    }

    [Fact]
    public void PrepareSpeedSamplesForPersistence_NullInput_ReturnsEmpty()
    {
        var result = DiskCardTestService.PrepareSpeedSamplesForPersistence(null);
        Assert.Empty(result);
    }

    [Fact]
    public void PrepareSpeedSamplesForPersistence_EmptyInput_ReturnsEmpty()
    {
        var result = DiskCardTestService.PrepareSpeedSamplesForPersistence(Array.Empty<SpeedSample>());
        Assert.Empty(result);
    }
}
