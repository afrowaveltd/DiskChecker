using System;
using System.Collections.Generic;
using System.Linq;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.ViewModels;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>Regression tests for certificate previews and cross-view selection.</summary>
public sealed class CertificatePreviewNavigationTests
{
    [Fact]
    public void NoMeasuredSeries_NeverDrawsInventedSpeedCurve()
    {
        var certificate = new DiskCertificate
        {
            TestType = "Seek", AvgWriteSpeed = 300, MaxWriteSpeed = 500,
            AvgReadSpeed = 450, MaxReadSpeed = 600
        };
        var graph = CertificateChartProjector.Build(certificate, [], []);
        Assert.Empty(graph.WriteProfilePoints);
        Assert.Empty(graph.ReadProfilePoints);
        Assert.Equal("N/A", graph.ChartMaxSpeedLabel);
    }

    [Fact]
    public void ReadOnlyScan_ShowsReadProfileAndZeroSpeedStallWithoutWriteCurve()
    {
        var certificate = new DiskCertificate { TestType = "FullRead", AvgWriteSpeed = 0 };
        var measured = new List<SpeedSample>
        {
            new() { ProgressPercent = 0, SpeedMBps = 100 },
            new() { ProgressPercent = 50, SpeedMBps = 0, IsStalled = true },
            new() { ProgressPercent = 100, SpeedMBps = 200 }
        };
        var graph = CertificateChartProjector.Build(certificate, [], measured);
        Assert.Empty(graph.WriteProfilePoints);
        Assert.Contains("471,226", graph.ReadProfilePoints, StringComparison.Ordinal);
        Assert.Equal("0 MB/s", graph.ChartMinSpeedLabel);
        Assert.Equal("200.0 MB/s", graph.ChartMaxSpeedLabel);
    }

    [Fact]
    public void PersistedReadOnlyProfile_StillRendersWhenSqlHasNoSamples()
    {
        var certificate = new DiskCertificate
        {
            TestType = "QuickRead",
            ReadProfilePoints = new List<double> { 50, 0, 70 }
        };
        var graph = CertificateChartProjector.Build(certificate, [], []);
        Assert.Empty(graph.WriteProfilePoints);
        Assert.Contains("471,226", graph.ReadProfilePoints, StringComparison.Ordinal);
    }

    [Fact]
    public void RealProgressCoordinatesTakePriorityOverEvenlySpacedHistoricalProfile()
    {
        var certificate = new DiskCertificate
        {
            TestType = "FullRead",
            ReadProfilePoints = new List<double> { 100, 150, 200 }
        };
        var measured = new List<SpeedSample>
        {
            new() { ProgressPercent = 10, SpeedMBps = 100 },
            new() { ProgressPercent = 80, SpeedMBps = 200 }
        };
        var graph = CertificateChartProjector.Build(certificate, [], measured);
        Assert.StartsWith("139,", graph.ReadProfilePoints, StringComparison.Ordinal);
        Assert.Contains("720,", graph.ReadProfilePoints, StringComparison.Ordinal);
    }

    [Fact]
    public void HundredsOfThousandsOfPointsRemainBoundedInProjection()
    {
        var samples = Enumerable.Range(0, 40_000)
            .Select(i => new SpeedSample
            {
                ProgressPercent = i * 100d / 39_999,
                SpeedMBps = i == 10_001 ? 0 : 300
            }).ToList();
        var graph = CertificateChartProjector.Build(
            new DiskCertificate { TestType = "FullRead" }, [], samples);

        Assert.InRange(graph.ReadProfilePoints.Split(' ').Length, 1,
            CertificateChartProjector.MaxSamplesPerPhase);
        Assert.Contains("226", graph.ReadProfilePoints, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectingAnotherDiskClearsStaleSessionAndCertificateIdentity()
    {
        var state = new SelectedDiskService
        {
            SelectedDisk = Drive("/dev/sda", "S1"),
            SelectedDiskCardId = 10,
            SelectedTestSessionId = 20,
            SelectedCertificateId = 30
        };

        state.SelectedDisk = Drive("/dev/sdb", "S1"); // same serial, different OS path
        Assert.Equal(10, state.SelectedDiskCardId);
        Assert.Equal(20, state.SelectedTestSessionId);
        Assert.Equal(30, state.SelectedCertificateId);

        state.SelectedDisk = Drive("/dev/sda", "S2"); // reused device path
        Assert.Null(state.SelectedDiskCardId);
        Assert.Null(state.SelectedTestSessionId);
        Assert.Null(state.SelectedCertificateId);
    }

    private static CoreDriveInfo Drive(string path, string serial) => new()
    {
        Path = path, Name = "Test disk", TotalSize = 80_000_000_000,
        SerialNumber = serial
    };
}
