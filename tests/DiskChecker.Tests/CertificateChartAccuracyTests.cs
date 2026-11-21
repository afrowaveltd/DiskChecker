using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.ViewModels;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>Certificate plot regressions: measured values, not synthetic shapes.</summary>
public sealed class CertificateChartAccuracyTests
{
    [Fact]
    public void SeekPhases_AreSeparateAndKeepTrueSampleIndices()
    {
        var samples = new[]
        {
            Point(SeekTestType.FullStroke, 1, 2),
            Point(SeekTestType.FullStroke, 5000, 20),
            Point(SeekTestType.Random, 1, 10),
            Point(SeekTestType.Random, 4000, 40),
            Point(SeekTestType.Skip, 2, 6)
        };

        var graph = CertificateSeekChartProjector.Build(samples);
        Assert.True(graph.HasGraph);
        Assert.Equal("5000", graph.EndLabel);
        Assert.Contains("56,", graph.FullStrokePoints);
        Assert.Contains("886,", graph.FullStrokePoints);
        Assert.Contains("56,", graph.RandomPoints);
        Assert.NotEmpty(graph.SkipPoints);
        Assert.Empty(graph.LegacyPoints);
        Assert.Equal(2, graph.FullStrokePoints.Split(' ').Length);
        Assert.Equal(2, graph.RandomPoints.Split(' ').Length);
    }

    [Fact]
    public void SeekInvalidAndErrorReadings_DoNotBecomeFakeData()
    {
        var graph = CertificateSeekChartProjector.Build(new[]
        {
            Point(SeekTestType.FullStroke, 1, double.NaN),
            new SeekSampleRecord
            {
                TestType = SeekTestType.Random, Index = 2, LatencyMs = 20,
                HasError = true
            },
            Point(SeekTestType.Skip, 3, -1)
        });
        Assert.False(graph.HasGraph);
        Assert.Equal("N/A", graph.MaxLabel);
    }

    [Fact]
    public void LargeSeekHistory_PreservesSpikeAndLastOriginalIndex()
    {
        var points = Enumerable.Range(1, 40000)
            .Select(i => Point(SeekTestType.FullStroke, i, i == 25006 ? 980 : 12))
            .ToArray();
        var graph = CertificateSeekChartProjector.Build(points);
        Assert.Equal("40000", graph.EndLabel);
        Assert.InRange(graph.FullStrokePoints.Split(' ').Length,
            1, CertificateSeekChartProjector.MaxSamplesPerMode);
        Assert.StartsWith("56,", graph.FullStrokePoints);
        Assert.Contains("886,", graph.FullStrokePoints);
        // The 980 ms spike must survive extremum-preserving downsampling.
        var top = graph.FullStrokePoints.Split(' ')
            .Select(p => double.Parse(p.Split(',')[1], CultureInfo.InvariantCulture))
            .Min();
        Assert.True(top < 50, $"Peak latency was lost: top pixel {top}");
    }

    [Fact]
    public void LegacySeekProfile_DoesNotClaimKnownSeekMode()
    {
        var graph = CertificateSeekChartProjector.FromLegacy(
            null, new List<double> { 1, 4, 2 });
        Assert.Empty(graph.FullStrokePoints);
        Assert.Empty(graph.RandomPoints);
        Assert.Empty(graph.SkipPoints);
        Assert.NotEmpty(graph.LegacyPoints);
    }

    [Fact]
    public void CelsiusOnlyTest_UsesDistinctAxisAndNeverDrawsInventedSpeed()
    {
        var certificate = new DiskCertificate { TestType = "Seek" };
        var graph = CertificateChartProjector.Build(certificate, [], [],
            new List<TemperatureSample>
            {
                new() { TemperatureCelsius = 25, ProgressPercent = 0 },
                new() { TemperatureCelsius = 55, ProgressPercent = 100 }
            });
        Assert.Empty(graph.ReadProfilePoints);
        Assert.Empty(graph.WriteProfilePoints);
        Assert.True(graph.HasTemperatureProfile);
        Assert.Contains("°C", graph.TemperatureMaxLabel);
        Assert.Equal("57 °C", graph.TemperatureMaxLabel);
        Assert.Equal("23 °C", graph.TemperatureMinLabel);
        Assert.Contains("886,", graph.TemperatureProfilePoints);
        Assert.Contains("pouze naměřený průběh teploty", graph.ChartSourceNote);
    }

    [Fact]
    public void RecordedPartialScan_RemainsPartialRatherThanInventingCompletion()
    {
        var samples = new List<SpeedSample>
        {
            new() { ProgressPercent = 0, SpeedMBps = 95 },
            new() { ProgressPercent = 50, SpeedMBps = 92 }
        };
        var graph = CertificateChartProjector.Build(
            new DiskCertificate { TestType = "FullRead" }, [], samples);
        Assert.Contains("471,", graph.ReadProfilePoints);
        Assert.DoesNotContain("886,", graph.ReadProfilePoints);
        Assert.Contains("50 %", graph.ChartSourceNote);
    }

    [Fact]
    public void LongHistoricalSpeedProfile_RetainsTheEndAndZeroSpeedStalls()
    {
        var history = Enumerable.Range(0, 3700)
            .Select(i => i == 3692 ? 0d : 95d).ToList();
        var certificate = new DiskCertificate
        {
            TestType = "FullRead",
            ReadProfilePoints = history
        };
        var graph = CertificateChartProjector.Build(certificate, [], []);
        var pixels = graph.ReadProfilePoints.Split(' ');

        Assert.InRange(pixels.Length, 1, CertificateChartProjector.MaxSamplesPerPhase);
        Assert.Contains("886,", graph.ReadProfilePoints);
        Assert.Contains("226", graph.ReadProfilePoints);
    }

    private static SeekSampleRecord Point(SeekTestType type, int index, double ms) => new()
    {
        TestType = type, Index = index, LatencyMs = ms
    };
}
