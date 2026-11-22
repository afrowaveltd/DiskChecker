using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Services;
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
