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
