using System;
using System.Collections.Generic;
using System.Linq;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Xunit;

namespace DiskChecker.Tests;

public class AdaptiveSpeedSamplerTests
{
    [Fact]
    public void AddSample_SteadySpeed_NoAnomalies()
    {
        var sampler = new AdaptiveSpeedSampler
        {
            AnomalyThresholdPercent = 15.0,
            HysteresisPercent = 5.0,
            MinAnomalyDurationMs = 100,
            BaselineWindowSize = 5
        };
        sampler.Initialize(1_000_000_000); // 1 GB

        var startTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Feed steady 100 MB/s samples
        for (int i = 0; i < 50; i++)
        {
            sampler.AddSample(100.0, i * 20_000_000, startTime.AddMilliseconds(i * 200));
        }

        sampler.FinalizePhase();

        Assert.Empty(sampler.Anomalies);
        Assert.NotEmpty(sampler.StandardSamples);
    }

    [Fact]
    public void AddSample_SuddenDrop_DetectsAnomaly()
    {
        var sampler = new AdaptiveSpeedSampler
        {
            AnomalyThresholdPercent = 15.0,
            HysteresisPercent = 5.0,
            MinAnomalyDurationMs = 100,
            BaselineWindowSize = 5,
            MinStandardIntervalMs = 50
        };
        sampler.Initialize(1_000_000_000);

        var startTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Steady 100 MB/s for 10 samples
        for (int i = 0; i < 10; i++)
            sampler.AddSample(100.0, i * 20_000_000, startTime.AddMilliseconds(i * 200));

        // Sudden drop to 50 MB/s (50% deviation) for 10 samples
        for (int i = 0; i < 10; i++)
            sampler.AddSample(50.0, (10 + i) * 20_000_000, startTime.AddMilliseconds((10 + i) * 200));

        // Recovery to 100 MB/s
        for (int i = 0; i < 10; i++)
            sampler.AddSample(100.0, (20 + i) * 20_000_000, startTime.AddMilliseconds((20 + i) * 200));

        sampler.FinalizePhase();

        Assert.NotEmpty(sampler.Anomalies);
        var anomaly = sampler.Anomalies[0];
        Assert.Equal("Write", anomaly.Phase);
        Assert.True(anomaly.MaxDeviationPercent > 15);
        Assert.True(anomaly.DurationMs > 0);
        Assert.True(anomaly.SeverityScore > 0);
    }


    [Fact]
    public void AddSample_SuddenDrop_StoresByteAndLbaRange()
    {
        var sampler = new AdaptiveSpeedSampler
        {
            AnomalyThresholdPercent = 15.0,
            HysteresisPercent = 5.0,
            MinAnomalyDurationMs = 100,
            BaselineWindowSize = 5,
            MinStandardIntervalMs = 50
        };
        sampler.Initialize(1_000_000_000);

        var startTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 10; i++)
            sampler.AddSample(100.0, i * 20_000_000L, startTime.AddMilliseconds(i * 200));
        for (int i = 0; i < 10; i++)
            sampler.AddSample(50.0, (10 + i) * 20_000_000L, startTime.AddMilliseconds((10 + i) * 200));
        for (int i = 0; i < 10; i++)
            sampler.AddSample(100.0, (20 + i) * 20_000_000L, startTime.AddMilliseconds((20 + i) * 200));

        sampler.FinalizePhase();

        var anomaly = Assert.Single(sampler.Anomalies);
        Assert.True(anomaly.StartBytesProcessed > 0);
        Assert.True(anomaly.EndBytesProcessed >= anomaly.StartBytesProcessed);
        Assert.Equal(anomaly.StartBytesProcessed / 512L, anomaly.StartLba512);
        Assert.Equal(anomaly.EndBytesProcessed / 512L, anomaly.EndLba512);
        Assert.True(anomaly.EndLba512 >= anomaly.StartLba512);
    }

    [Fact]
    public void AddSample_BriefSpike_FilteredByMinDuration()
    {
        var sampler = new AdaptiveSpeedSampler
        {
            AnomalyThresholdPercent = 15.0,
            HysteresisPercent = 5.0,
            MinAnomalyDurationMs = 2000, // High threshold — only sustained anomalies pass
            BaselineWindowSize = 5,
            MinStandardIntervalMs = 50
        };
        sampler.Initialize(1_000_000_000);

        var startTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Steady 100 MB/s for 20 samples (builds solid baseline)
        for (int i = 0; i < 20; i++)
            sampler.AddSample(100.0, i * 20_000_000, startTime.AddMilliseconds(i * 200));

        // Brief spike: 3 samples at 200 MB/s = 600ms (below 2000ms threshold)
        for (int i = 0; i < 3; i++)
            sampler.AddSample(200.0, (20 + i) * 20_000_000, startTime.AddMilliseconds(4000 + i * 200));

        // Back to steady for many samples to let any false anomaly resolve
        for (int i = 0; i < 30; i++)
            sampler.AddSample(100.0, (23 + i) * 20_000_000, startTime.AddMilliseconds(4600 + i * 200));

        sampler.FinalizePhase();

        // Brief spike should be filtered out — duration < 2000ms
        Assert.Empty(sampler.Anomalies);
    }

    [Fact]
    public void PhaseChange_FinalizesAnomaly()
    {
        var sampler = new AdaptiveSpeedSampler
        {
            AnomalyThresholdPercent = 15.0,
            HysteresisPercent = 5.0,
            MinAnomalyDurationMs = 100,
            BaselineWindowSize = 5,
            MinStandardIntervalMs = 50
        };
        sampler.Initialize(1_000_000_000);

        var startTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Steady write
        for (int i = 0; i < 10; i++)
            sampler.AddSample(100.0, i * 20_000_000, startTime.AddMilliseconds(i * 200));

        // Drop during write
        for (int i = 0; i < 10; i++)
            sampler.AddSample(50.0, (10 + i) * 20_000_000, startTime.AddMilliseconds((10 + i) * 200));

        // Switch to Read phase (should finalize write anomaly)
        sampler.Phase = "Read";
        sampler.Initialize(1_000_000_000);

        // Steady read
        for (int i = 0; i < 20; i++)
            sampler.AddSample(120.0, i * 20_000_000, startTime.AddMilliseconds(5000 + i * 200));

        sampler.FinalizePhase();

        // Should have 1 write anomaly
        var writeAnomalies = sampler.Anomalies.Where(a => a.Phase == "Write").ToList();
        Assert.Single(writeAnomalies);
    }

    [Fact]
    public void FinalizePhase_DownsamplesToTarget()
    {
        var sampler = new AdaptiveSpeedSampler
        {
            TargetStandardSamples = 20,
            MinStandardIntervalMs = 10
        };
        sampler.Initialize(1_000_000_000);

        var startTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Feed 200 samples (more than target)
        for (int i = 0; i < 200; i++)
            sampler.AddSample(100.0, i * 5_000_000, startTime.AddMilliseconds(i * 50));

        sampler.FinalizePhase();

        Assert.True(sampler.StandardSamples.Count <= 20);
    }
}

public class AnomalyAnalysisServiceTests
{
    [Fact]
    public void FindOverlappingAnomalies_Overlapping_ReturnsPairs()
    {
        var service = new AnomalyAnalysisService();

        var writeAnomaly = new SpeedAnomaly
        {
            Phase = "Write",
            StartProgressPercent = 40,
            EndProgressPercent = 50,
            MinSpeedMBps = 30,
            MaxSpeedMBps = 100,
            EntrySpeedMBps = 100,
            MaxDeviationPercent = 70,
            DurationMs = 2000
        };

        var readAnomaly = new SpeedAnomaly
        {
            Phase = "Read",
            StartProgressPercent = 42,
            EndProgressPercent = 52,
            MinSpeedMBps = 25,
            MaxSpeedMBps = 110,
            EntrySpeedMBps = 110,
            MaxDeviationPercent = 77,
            DurationMs = 2500
        };

        var pairs = service.FindOverlappingAnomalies(new List<SpeedAnomaly> { writeAnomaly, readAnomaly });
        Assert.Single(pairs);
        Assert.Equal(writeAnomaly, pairs[0].Write);
        Assert.Equal(readAnomaly, pairs[0].Read);
    }

    [Fact]
    public void FindOverlappingAnomalies_NonOverlapping_ReturnsEmpty()
    {
        var service = new AnomalyAnalysisService();

        var writeAnomaly = new SpeedAnomaly
        {
            Phase = "Write",
            StartProgressPercent = 10,
            EndProgressPercent = 20,
            MaxDeviationPercent = 50,
            DurationMs = 1000
        };

        var readAnomaly = new SpeedAnomaly
        {
            Phase = "Read",
            StartProgressPercent = 80,
            EndProgressPercent = 90,
            MaxDeviationPercent = 50,
            DurationMs = 1000
        };

        var pairs = service.FindOverlappingAnomalies(new List<SpeedAnomaly> { writeAnomaly, readAnomaly });
        Assert.Empty(pairs);
    }

    [Fact]
    public void ComputeCorrelationScore_HighOverlap_HighScore()
    {
        var service = new AnomalyAnalysisService();

        var write = new SpeedAnomaly
        {
            Phase = "Write",
            StartProgressPercent = 40,
            EndProgressPercent = 50,
            MinSpeedMBps = 30,
            MaxSpeedMBps = 100,
            EntrySpeedMBps = 100,
            MaxDeviationPercent = 70,
            DurationMs = 2000
        };

        var read = new SpeedAnomaly
        {
            Phase = "Read",
            StartProgressPercent = 42,
            EndProgressPercent = 52,
            MinSpeedMBps = 25,
            MaxSpeedMBps = 110,
            EntrySpeedMBps = 110,
            MaxDeviationPercent = 75,
            DurationMs = 2200
        };

        var score = service.ComputeCorrelationScore(write, read);
        Assert.True(score > 70, $"Expected >70, got {score}"); // High correlation expected
    }

    [Fact]
    public void ComputeAnomalyPenalty_NoAnomalies_Zero()
    {
        var service = new AnomalyAnalysisService();
        var penalty = service.ComputeAnomalyPenalty(new List<SpeedAnomaly>());
        Assert.Equal(0, penalty);
    }

    [Fact]
    public void ComputeAnomalyPenalty_WithAnomalies_ReturnsPenalty()
    {
        var service = new AnomalyAnalysisService();

        var anomalies = new List<SpeedAnomaly>
        {
            new()
            {
                Phase = "Write",
                StartProgressPercent = 30,
                EndProgressPercent = 35,
                MaxDeviationPercent = 80,
                DurationMs = 5000,
                SeverityScore = 85
            },
            new()
            {
                Phase = "Read",
                StartProgressPercent = 32,
                EndProgressPercent = 37,
                MaxDeviationPercent = 75,
                DurationMs = 4500,
                SeverityScore = 80
            }
        };

        var penalty = service.ComputeAnomalyPenalty(anomalies);
        Assert.True(penalty > 0, "Should have penalty for anomalies");
        Assert.True(penalty <= 50, "Penalty should not exceed 50");
    }

    [Fact]
    public void GenerateAnomalyReport_WithAnomalies_ReturnsNonEmpty()
    {
        var service = new AnomalyAnalysisService();

        var anomalies = new List<SpeedAnomaly>
        {
            new()
            {
                Phase = "Write",
                StartProgressPercent = 30,
                EndProgressPercent = 35,
                MinSpeedMBps = 20,
                MaxSpeedMBps = 100,
                EntrySpeedMBps = 100,
                MaxDeviationPercent = 80,
                DurationMs = 5000,
                SeverityScore = 85
            }
        };

        var report = service.GenerateAnomalyReport(anomalies);
        Assert.Contains("Detekováno", report);
        Assert.Contains("Zápis", report);
    }
}
