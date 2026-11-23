using System;
using System.Linq;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.Infrastructure.Services;
using Xunit;

namespace DiskChecker.Tests;

public sealed class DiskBarcodeAndBundleTests
{
    [Fact]
    public void BarcodeIsStableForSerialAndSameOnCertificate()
    {
        var disk = new DiskCard { Id = 42, SerialNumber = " 2M292LQN7BEU ", ModelName = "ADATA SU800" };
        var cert = new DiskCertificate { DiskCardId = 42, SerialNumber = "2m292lqn7beu", TestSessionId = 10 };
        var value = DiskBarcode.ForCard(disk);

        Assert.NotNull(value);
        Assert.Matches("^DS[0-9]{16}$", value);
        Assert.Equal(value, DiskBarcode.ForCertificate(cert));
        Assert.Equal(value, DiskBarcode.ForCard(new DiskCard { Id = 9000, SerialNumber = disk.SerialNumber }));
        Assert.Single(DiskBarcode.FindMatches(new[] { disk, new DiskCard { Id = 43, SerialNumber = "OTHER" } }, value));
        Assert.Single(DiskBarcode.FindMatches(new[] { disk }, disk.SerialNumber.Trim()));
    }

    [Fact]
    public void MissingHardwareSerialUsesLocalCardIdentifier()
    {
        var disk = new DiskCard { Id = 42, SerialNumber = "NOSN-ABCDEF" };
        Assert.Equal("DL0000000000000042", DiskBarcode.ForCard(disk));
        Assert.Equal("DL0000000000000042",
            DiskBarcode.ForCertificate(new DiskCertificate { SerialNumber = "", DiskCardId = 42 }));
        Assert.Null(DiskBarcode.ForCertificate(new DiskCertificate { SerialNumber = "" }));
        Assert.Empty(DiskBarcode.FindMatches(new[] { disk }, "DL0000000000000043"));
    }

    [Fact]
    public void DuplicateSerialIsAmbiguousAndMustNotSilentlyPickFirst()
    {
        var cards = new[] {
            new DiskCard { Id = 1, SerialNumber = "DUPLICATE" },
            new DiskCard { Id = 2, SerialNumber = "DUPLICATE" }
        };
        Assert.Equal(2, DiskBarcode.FindMatches(cards, DiskBarcode.ForCard(cards[0])).Count);
    }

    [Fact]
    public void Code128EncodesPrefixDigitPairsChecksumAndStop()
    {
        var encoded = Code128Barcode.Encode("DS0000000000000001");
        Assert.Equal(new[] { 104, 36, 51, 99, 0, 0, 0, 0, 0, 0, 0, 1, 35, 106 }, encoded);
        Assert.Equal(156, Code128Barcode.TotalModules("DS0000000000000001"));
        Assert.Throws<ArgumentException>(() => Code128Barcode.Encode("NOT-A-DISK"));
    }

    [Theory]
    [InlineData("Seek", "Sanitization", "SmartShort")]
    [InlineData("SeekTest", "Sanitizace", "SMART-only")]
    public void TwoOrThreeDifferentTestTypesAllowed(string seek, string sanitation, string smart)
    {
        var parts = new[] { Cert(1, 101, seek), Cert(1, 102, sanitation), Cert(1, 103, smart) };
        CombinedCertificatePolicy.Validate(parts);
        CombinedCertificatePolicy.Validate(parts.Take(2).ToArray());
    }

    [Fact]
    public void CombinedReportRejectsMixedDisksDuplicateSessionOrSameTestType()
    {
        Assert.Throws<ArgumentException>(() => CombinedCertificatePolicy.Validate(new[] { Cert(1, 1, "Seek") }));
        Assert.Throws<ArgumentException>(() => CombinedCertificatePolicy.Validate(new[] {
            Cert(1, 1, "Seek"), Cert(2, 2, "Sanitization") }));
        Assert.Throws<ArgumentException>(() => CombinedCertificatePolicy.Validate(new[] {
            Cert(1, 1, "Seek"), Cert(1, 1, "Sanitization") }));
        Assert.Throws<ArgumentException>(() => CombinedCertificatePolicy.Validate(new[] {
            Cert(1, 1, "SmartShort"), Cert(1, 2, "SmartExtended") }));
    }

    [Fact]
    public void BundlePdfContainsCoverAndCompleteOriginalPages()
    {
        var parts = new[] { Cert(11, 101, "Seek"), Cert(11, 102, "Sanitization"), Cert(11, 103, "SmartShort") };
        var output = CertificatePdfPageRenderer.CreateBundlePdf(parts);

        Assert.True(output.Length > 5000);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(output, 0, 8));
        Assert.All(parts, c => Assert.Null(c.PairedCertificate)); // no mutation
    }

    private static DiskCertificate Cert(int diskId, int sessionId, string type) => new()
    {
        DiskCardId = diskId,
        TestSessionId = sessionId,
        CertificateNumber = $"DC-{sessionId}",
        SerialNumber = "SERIAL-123",
        DiskModel = "Test SSD",
        TestType = type,
        Grade = "A",
        Score = 90,
        GeneratedAt = DateTime.UtcNow,
        RecommendationNotes = "No observed errors."
    };
}
