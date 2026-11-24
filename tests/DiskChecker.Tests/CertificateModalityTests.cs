using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Services;
using NSubstitute;
using Xunit;
using TestResult = DiskChecker.Core.Models.TestResult;

namespace DiskChecker.Tests;

/// <summary>
/// Per-test semantics and PDF layout regressions. No hardware is touched.
/// </summary>
public sealed class CertificateModalityTests
{
    private static CertificateGenerator NewGenerator()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);
        return new CertificateGenerator(settingsService: settings);
    }

    private static DiskCard Card(int id = 202) => new()
    {
        Id = id,
        ModelName = "Synthetic Test Disk",
        SerialNumber = "CERT-READ-ONLY",
        Capacity = 80_000_000_000,
        DiskType = "HDD",
        InterfaceType = "SATA",
        FirmwareVersion = "1.0"
    };

    [Theory]
    [InlineData(TestType.QuickRead)]
    [InlineData(TestType.FullRead)]
    public async Task ReadOnlySession_NeverUsesUnmeasuredWriteSpeedForGrade(TestType kind)
    {
        var test = new TestSession
        {
            Id = 301,
            DiskCardId = 202,
            TestType = kind,
            Status = TestStatus.Completed,
            Result = TestResult.Pass,
            Grade = "A",
            Score = 96,
            BytesWritten = 0,
            BytesRead = 80_000_000_000,
            AverageWriteSpeedMBps = 0,
            MinWriteSpeedMBps = 0,
            MaxWriteSpeedMBps = 0,
            AverageReadSpeedMBps = 88,
            MinReadSpeedMBps = 82,
            MaxReadSpeedMBps = 97,
            SmartBefore = new SmartaData { IsHealthy = true, SerialNumber = "CERT-READ-ONLY" }
        };

        var generator = NewGenerator();
        var calculated = generator.CalculateGrade(test);
        var certificate = await generator.GenerateCertificateAsync(test, Card());

        Assert.Equal("A", calculated.grade);
        Assert.Equal("A", certificate.Grade);
        Assert.InRange(certificate.Score, 90, 100);
        Assert.True(certificate.IsReadOnlyTest);
        Assert.Equal(0, certificate.AvgWriteSpeed);
        Assert.False(certificate.DataVerified);
        Assert.DoesNotContain("SELHAL", certificate.RecommendationNotes ?? string.Empty);
    }

    [Fact]
    public async Task SeekOnlySession_PreservesLatencyBasedGradeWithoutSpeedPenalty()
    {
        var test = new TestSession
        {
            Id = 302,
            DiskCardId = 202,
            TestType = TestType.Seek,
            Status = TestStatus.Completed,
            Result = TestResult.Pass,
            Grade = "B",
            Score = 86,
            AverageWriteSpeedMBps = 0,
            AverageReadSpeedMBps = 0,
            SmartBefore = new SmartaData { IsHealthy = true }
        };

        var certificate = await NewGenerator().GenerateCertificateAsync(test, Card());

        Assert.Equal("B", certificate.Grade);
        Assert.Equal(86, certificate.Score);
        Assert.Equal(0, certificate.AvgWriteSpeed);
        Assert.False(certificate.SanitizationPerformed);
    }

    [Fact]
    public async Task SlowSeekWithoutIoOrSmartFaults_DoesNotDeclareDiskBroken()
    {
        var test = new TestSession
        {
            Id = 303, DiskCardId = 202, TestType = TestType.Seek,
            Status = TestStatus.Completed, Result = TestResult.Pass,
            Grade = "E", Score = 18,
            SmartBefore = new SmartaData { IsHealthy = true }
        };
        var certificate = await NewGenerator().GenerateCertificateAsync(test, Card());

        Assert.Equal("E", certificate.Grade);
        Assert.Contains("Samotná vyšší latence není důkazem fyzické poruchy",
            certificate.RecommendationNotes);
        Assert.DoesNotContain("okamžitě vyřadit", certificate.RecommendationNotes,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompletedSlowSanitization_IsNotMisrepresentedAsPhysicalFailure()
    {
        var test = new TestSession
        {
            Id = 304, DiskCardId = 202, TestType = TestType.Sanitization,
            Status = TestStatus.Completed, Result = TestResult.Pass,
            Grade = "E", Score = 47,
            HealthAssessment = HealthAssessment.Poor,
            AverageWriteSpeedMBps = 40.9, AverageReadSpeedMBps = 383.3,
            SmartBefore = new SmartaData { IsHealthy = true },
            SmartAfter = new SmartaData { IsHealthy = true }
        };
        var certificate = await NewGenerator().GenerateCertificateAsync(test, Card());

        Assert.Equal("E", certificate.Grade);
        Assert.False(certificate.Recommended);
        Assert.True(certificate.DataVerified);
        Assert.Contains("nízký výkon sám o sobě neprokazuje fyzickou poruchu", certificate.RecommendationNotes);
        Assert.DoesNotContain("okamžitě vyřadit", certificate.RecommendationNotes, StringComparison.OrdinalIgnoreCase);

        test.SmartAfter.IsFailing = true;
        var failing = await NewGenerator().GenerateCertificateAsync(test, Card());
        Assert.False(failing.Recommended);
        Assert.Contains("okamžitě vyřadit", failing.RecommendationNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SeekChartCoverage_SeparatesPlottedPointsFromAllMeasurements()
    {
        var certificate = new DiskCertificate { SeekTotalCount = 3100 };
        Assert.Equal("Body grafu: 445; měření celkem: 3100",
            CertificatePdfPageRenderer.SeekChartCoverageText(certificate, 445));
        certificate.SeekTotalCount = null;
        Assert.Equal("Body grafu: 445; měření celkem: N/A",
            CertificatePdfPageRenderer.SeekChartCoverageText(certificate, 445));
    }

    [Fact]
    public async Task CombinedPdf_HasSinglePageAndDoesNotRewriteIndependentTestGrades()
    {
        string root = Path.Combine(Path.GetTempPath(), "DiskChecker-Modality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = Substitute.For<ISettingsService>();
            settings.GetCertificatePathAsync().Returns(root);
            var generator = new CertificateGenerator(settingsService: settings);
            var surface = new DiskCertificate
            {
                DiskCardId = 202, TestSessionId = 301,
                CertificateNumber = "PAIR-301-302", GeneratedAt = DateTime.UtcNow,
                DiskModel = "Synthetic Test Disk", SerialNumber = "CERT-READ-ONLY",
                Capacity = "80 GB", DiskType = "HDD", TestType = "FullRead",
                Grade = "A", Score = 96, AvgReadSpeed = 88, AvgWriteSpeed = 0,
                RecommendationNotes = "Read-only scan successful",
                TechnicianAssessment = "Inspected without changing the source disk",
                ReadProfilePoints = new() { 80, 83, 89, 88, 90, 85 }
            };
            var seek = new DiskCertificate
            {
                DiskCardId = 202, TestSessionId = 302,
                TestType = "Seek", Grade = "C", Score = 77,
                SeekAvgLatencyMs = 16.5, SeekP95LatencyMs = 21.0,
                SeekLatencyPoints = new() { 15.0, 16.5, 21.0, 17.0 },
                RecommendationNotes = "Slow tail latency",
                TechnicianAssessment = "Observe seek outliers"
            };
            surface.PairedCertificate = seek;
            var pdf = await generator.GeneratePdfAsync(surface);
            var qaFolder = Environment.GetEnvironmentVariable("DISKCHECKER_PDF_QA_DIR");
            if (!string.IsNullOrWhiteSpace(qaFolder))
            {
                Directory.CreateDirectory(qaFolder);
                File.Copy(pdf, Path.Combine(qaFolder, "combined-surface-seek.pdf"), overwrite: true);
            }
            var text = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(pdf, TestContext.Current.CancellationToken));

            Assert.StartsWith("%PDF-", text, StringComparison.Ordinal);
            Assert.Contains("/Count 1", text, StringComparison.Ordinal);
            Assert.Equal("A", surface.Grade);
            Assert.Equal("C", seek.Grade);
            Assert.False(surface.DataVerified);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CombinedPdf_RejectsDifferentPhysicalDiskIdentity()
    {
        var generator = NewGenerator();
        var first = new DiskCertificate
        {
            DiskCardId = 101, TestSessionId = 401, GeneratedAt = DateTime.UtcNow,
            SerialNumber = "A", TestType = "FullRead", Grade = "A",
            PairedCertificate = new DiskCertificate
            {
                DiskCardId = 202, TestSessionId = 402, TestType = "Seek", Grade = "A"
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GeneratePdfAsync(first));
    }
}
