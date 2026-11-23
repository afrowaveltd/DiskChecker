using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Services;
using DiskChecker.UI.Avalonia.ViewModels;
using LiveChartsCore.Defaults;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// A human-readable localized test name must not turn full destructive
/// or safe destructive PDF certificates into seek-only reports.
/// </summary>
public sealed class LocalizedCertificateCategoryTests
{
    [Theory]
    [InlineData("Absolutní destruktivní test")]
    [InlineData("Bezpečný destruktivní test (záloha → test → obnova)")]
    [InlineData("AbsoluteDestructive")]
    [InlineData("Full destructive disk analysis")]
    public void SanitizationWithSeekHasComprehensivePdfCategory(string displayType)
    {
        var certificate = new DiskCertificate
        {
            TestType = displayType,
            SanitizationPerformed = true,
            SeekAvgLatencyMs = 18.5,
            AvgReadSpeed = 45,
            AvgWriteSpeed = 41,
            SeekLatencyPoints = new() { 12, 22, 31 },
            ReadProfilePoints = new() { 42, 43, 45 }
        };

        Assert.Equal("KOMPLEXNÍ DESTRUKTIVNÍ TEST",
            CertificatePdfPageRenderer.Name(certificate));
        Assert.False(CertificatePdfPageRenderer.IsSeekOnly(certificate));
    }

    [Fact]
    public void SafeDestructiveChartSamplingKeepsActualOrderAndBoundedMemory()
    {
        var measured = Enumerable.Range(0, 100)
            .Select(i => new ObservablePoint(i, 100 - i)).ToList();
        var samples = SafeDestructiveTestViewModel.DownsampleCertificateSeries(measured);
        Assert.Equal(32, samples.Count);
        Assert.Equal(100d, samples[0]);
        Assert.Equal(4d, samples[^1]);
        Assert.Equal(100, measured.Count);
    }

    [Fact]
    public void SafeDestructiveChartSamplingHandlesEmptyDataAndRejectsInvalidBudget()
    {
        Assert.Empty(SafeDestructiveTestViewModel.DownsampleCertificateSeries(
            new List<ObservablePoint>()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SafeDestructiveTestViewModel.DownsampleCertificateSeries(
                new List<ObservablePoint>(), 0));
    }

    [Fact]
    public void PureSeekKeepsSeekOnlyCategory()
    {
        var certificate = new DiskCertificate
        {
            TestType = "Seek",
            SeekAvgLatencyMs = 16.7,
            SeekLatencyPoints = new() { 15, 18, 20 }
        };
        Assert.True(CertificatePdfPageRenderer.IsSeekOnly(certificate));
        Assert.Equal("SEEK / LATENCE",
            CertificatePdfPageRenderer.Name(certificate));
    }

    [Fact]
    public void SanitizationWithoutSeekKeepsSanitizationCategory()
    {
        var certificate = new DiskCertificate
        {
            TestType = "Sanitizace",
            SanitizationPerformed = true,
            AvgReadSpeed = 40,
            AvgWriteSpeed = 38
        };
        Assert.False(CertificatePdfPageRenderer.IsSeekOnly(certificate));
        Assert.Equal("SANITIZACE / OVĚŘENÍ",
            CertificatePdfPageRenderer.Name(certificate));
    }

    [Fact]
    public void SanitizationCertificateSeparatesIoCurveFromReadVerifyThroughput()
    {
        var certificate = new DiskCertificate
        {
            TestType = "Sanitization",
            SanitizationPerformed = true,
            AvgReadSpeed = 328.7,
            AvgWriteSpeed = 311.5,
            ReadProfilePoints = new() { 428, 429, 428 },
            WriteProfilePoints = new() { 310, 360, 330 }
        };

        // The graph is instantaneous I/O throughput; its values must not be
        // silently replaced with whole-phase rates or mixed between phases.
        Assert.Equal(429, certificate.ReadProfilePoints[1]);
        Assert.Equal(328.7, certificate.AvgReadSpeed);
        Assert.Contains("vč. ověření", CertificatePdfPageRenderer.ReadAverageText(certificate));
        Assert.Contains("328", CertificatePdfPageRenderer.ReadAverageText(certificate));
        Assert.Contains("bez pauz", CertificatePdfPageRenderer.WriteAverageText(certificate));
        Assert.Contains("311", CertificatePdfPageRenderer.WriteAverageText(certificate));
        Assert.Contains("MiB/s", CertificatePdfPageRenderer.ReadAverageText(certificate));
        Assert.Contains("I/O (MiB/s)", CertificatePdfPageRenderer.SpeedChartTitle(certificate));
        Assert.Contains("ověřování nul",
            CertificatePdfPageRenderer.ScopeMethodologyNote(certificate));
        Assert.Contains("plánované pauzy",
            CertificatePdfPageRenderer.ScopeMethodologyNote(certificate));
    }

    [Fact]
    public void NonSanitizationCertificateRetainsExistingSpeedLabelsAndLimitations()
    {
        var certificate = new DiskCertificate
        {
            TestType = "QuickRead",
            AvgReadSpeed = 120
        };
        Assert.Contains("Čtení průměr:", CertificatePdfPageRenderer.ReadAverageText(certificate));
        Assert.DoesNotContain("vč. ověření", CertificatePdfPageRenderer.ReadAverageText(certificate));
        Assert.Contains("MB/s", CertificatePdfPageRenderer.ReadAverageText(certificate));
        Assert.Contains("N/A (bez zápisu)", CertificatePdfPageRenderer.WriteAverageText(certificate));
        Assert.Equal("PRŮBĚH RYCHLOSTI (MB/s)",
            CertificatePdfPageRenderer.SpeedChartTitle(certificate));
        Assert.Contains("Rychlost zápisu se nehodnotí při testu čtení",
            CertificatePdfPageRenderer.ScopeMethodologyNote(certificate));
    }

    [Fact]
    public void ReadOnlyTestWithSeekDataRemainsReadOnly()
    {
        var certificate = new DiskCertificate
        {
            TestType = "QuickRead",
            AvgReadSpeed = 45,
            SeekAvgLatencyMs = 14
        };
        Assert.False(CertificatePdfPageRenderer.IsSeekOnly(certificate));
        Assert.Equal("ČTENÍ BEZ ZÁPISU",
            CertificatePdfPageRenderer.Name(certificate));
    }
}
