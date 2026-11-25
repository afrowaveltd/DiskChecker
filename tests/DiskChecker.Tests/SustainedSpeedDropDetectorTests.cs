using System;
using DiskChecker.Core.Services;
using Xunit;

namespace DiskChecker.Tests;

public class SustainedSpeedDropDetectorTests
{
    [Fact]
    public void NaturalZonesMayDeclineWithoutGlobalLimit()
    {
        var detector = new SustainedSpeedDropDetector();
        foreach (var speed in new[] {185d, 165, 160, 155, 140, 120, 100, 80, 60, 45})
            detector.Observe(speed, TimeSpan.FromSeconds(2));
        Assert.Equal(0, detector.Count);
    }

    [Theory]
    [InlineData(499, 0)]
    [InlineData(500, 0)]
    [InlineData(501, 1)]
    [InlineData(1000, 1)]
    public void StrictDurationBoundary(int milliseconds, int expected)
    {
        var detector = new SustainedSpeedDropDetector();
        detector.Observe(100, TimeSpan.FromSeconds(1));
        detector.Observe(60, TimeSpan.FromMilliseconds(milliseconds));
        detector.Observe(100, TimeSpan.FromSeconds(1));
        Assert.Equal(expected, detector.Count);
    }

    [Fact]
    public void ExactlyThirtyPercentIsNotADrop()
    {
        var detector = new SustainedSpeedDropDetector();
        detector.Observe(100, TimeSpan.FromSeconds(1));
        detector.Observe(70, TimeSpan.FromSeconds(2));
        Assert.Equal(0, detector.Count);
    }

    [Fact]
    public void PersistentDropCountsOnlyOnce()
    {
        var detector = new SustainedSpeedDropDetector();
        detector.Observe(100, TimeSpan.FromSeconds(1));
        for (int j = 0; j < 30; j++)
            detector.Observe(60, TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, detector.Count);
        Assert.Equal(0.4, detector.WorstConfirmedDropRatio, 6);
    }

    [Fact]
    public void SeparateBriefDipsDoNotAccumulate()
    {
        var detector = new SustainedSpeedDropDetector();
        detector.Observe(100, TimeSpan.FromSeconds(1));
        for (int j = 0; j < 5; j++)
        {
            detector.Observe(50, TimeSpan.FromMilliseconds(300));
            detector.Observe(100, TimeSpan.FromMilliseconds(300));
        }
        Assert.Equal(0, detector.Count);
    }

    [Fact]
    public void PhaseBoundaryDiscardsPendingEvidence()
    {
        var detector = new SustainedSpeedDropDetector();
        detector.Observe(100, TimeSpan.FromSeconds(1));
        detector.Observe(50, TimeSpan.FromMilliseconds(300));
        detector.ResetBaseline();
        detector.Observe(50, TimeSpan.FromMilliseconds(400));
        Assert.Equal(0, detector.Count);
    }
}
