using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public sealed class CombinedCertificateHistoryExportTests
{
    private static readonly DiskCard Card = new()
    {
        Id = 42,
        ModelName = "WDC WD10 EZEX",
        SerialNumber = "WDC-SERIAL-123",
        DiskType = "HDD",
        Capacity = 1_000_000_000_000
    };

    [Fact]
    public void HistoryMultiSelectionIsUsedWhenNoCertificatesAreChecked()
    {
        var chosen = CombinedCertificateExportService.SelectSessions(
            new[] { 101, 102 },
            Array.Empty<DiskCertificate>());

        Assert.Equal(new[] { 101, 102 }, chosen);
    }

    [Fact]
    public void CheckedCertificatesKeepTheirExistingWorkflow()
    {
        var certs = new[]
        {
            new DiskCertificate { Id = 10, TestSessionId = 202 },
            new DiskCertificate { Id = 11, TestSessionId = 203 }
        };

        var chosen = CombinedCertificateExportService.SelectSessions(
            new[] { 101, 102 }, certs);

        Assert.Equal(new[] { 202, 203 }, chosen);
    }

    [Fact]
    public async Task LegacyHistoryRowsWithoutCertificateCanProduceCombinedPdfWithoutDatabaseWrites()
    {
        var repository = Substitute.For<IDiskCardRepository>();
        var generator = Substitute.For<ICertificateGenerator>();
        var seek = Session(101, TestType.Seek);
        var sanitize = Session(102, TestType.Sanitization);

        repository.GetTestSessionWithoutSamplesAsync(101).Returns(seek);
        repository.GetTestSessionWithoutSamplesAsync(102).Returns(sanitize);
        repository.GetCertificatesAsync(Card.Id).Returns(new List<DiskCertificate>());
        ArrangeEmptySamples(repository);
        ArrangeGenerator(generator);
        IReadOnlyList<DiskCertificate>? captured = null;
        generator.GenerateCombinedPdfAsync(Arg.Any<IReadOnlyList<DiskCertificate>>())
            .Returns(info =>
            {
                captured = ((IReadOnlyList<DiskCertificate>)info[0]).ToArray();
                return Task.FromResult("combined.pdf");
            });

        var result = await new CombinedCertificateExportService(repository, generator)
            .ExportAsync(Card, new[] { 101, 102 });

        Assert.Equal("combined.pdf", result);
        Assert.NotNull(captured);
        Assert.Equal(2, captured.Count);
        Assert.Equal(new[] { 101, 102 }, captured.Select(c => c.TestSessionId));
        Assert.All(captured, c => Assert.Equal(Card.Id, c.DiskCardId));
        Assert.Equal("HIST-42-101", captured[0].CertificateNumber);
        Assert.Equal("HIST-42-102", captured[1].CertificateNumber);

        // Generating a bundle from old history MUST NOT write certificates
        // back into the application's database or replace original results.
        await repository.DidNotReceive().CreateCertificateAsync(Arg.Any<DiskCertificate>());
        await repository.DidNotReceive().UpdateCertificateAsync(Arg.Any<DiskCertificate>());
    }

    [Fact]
    public async Task MixedDiskTestSessionsAreRejectedBeforePdfCreation()
    {
        var repository = Substitute.For<IDiskCardRepository>();
        var generator = Substitute.For<ICertificateGenerator>();
        repository.GetTestSessionWithoutSamplesAsync(101).Returns(Session(101, TestType.Seek));
        repository.GetTestSessionWithoutSamplesAsync(102).Returns(Session(102, TestType.Sanitization, diskId: 43));
        repository.GetCertificatesAsync(Card.Id).Returns(new List<DiskCertificate>());
        ArrangeEmptySamples(repository);
        ArrangeGenerator(generator);

        var service = new CombinedCertificateExportService(repository, generator);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExportAsync(Card, new[] { 101, 102 }));
        await generator.DidNotReceive().GenerateCombinedPdfAsync(
            Arg.Any<IReadOnlyList<DiskCertificate>>());
    }

    [Fact]
    public async Task ExistingCertificateIsPreferredForHistoricalTest()
    {
        var repository = Substitute.For<IDiskCardRepository>();
        var generator = Substitute.For<ICertificateGenerator>();
        var existing = new DiskCertificate
        {
            Id = 55, DiskCardId = Card.Id, TestSessionId = 101,
            CertificateNumber = "ORIGINAL-55", TestType = "Seek",
            Grade = "B", Score = 64
        };

        repository.GetCertificatesAsync(Card.Id).Returns(new List<DiskCertificate> { existing });
        repository.GetCertificateAsync(55).Returns(existing);
        repository.GetTestSessionWithoutSamplesAsync(101).Returns(Session(101, TestType.Seek));
        repository.GetTestSessionWithoutSamplesAsync(102).Returns(Session(102, TestType.Sanitization));
        ArrangeEmptySamples(repository);
        ArrangeGenerator(generator);
        IReadOnlyList<DiskCertificate>? captured = null;
        generator.GenerateCombinedPdfAsync(Arg.Any<IReadOnlyList<DiskCertificate>>())
            .Returns(info =>
            {
                captured = ((IReadOnlyList<DiskCertificate>)info[0]).ToArray();
                return Task.FromResult("combined.pdf");
            });

        await new CombinedCertificateExportService(repository, generator)
            .ExportAsync(Card, new[] { 101, 102 });

        Assert.NotNull(captured);
        Assert.Equal("ORIGINAL-55", captured[0].CertificateNumber);
        Assert.Equal("B", captured[0].Grade);
        Assert.Equal(64, captured[0].Score);
        Assert.Equal("HIST-42-102", captured[1].CertificateNumber);
    }

    [Fact]
    public async Task DuplicateTestKindsAreRejectedForHistoryExport()
    {
        var repository = Substitute.For<IDiskCardRepository>();
        var generator = Substitute.For<ICertificateGenerator>();
        repository.GetCertificatesAsync(Card.Id).Returns(new List<DiskCertificate>());
        repository.GetTestSessionWithoutSamplesAsync(101).Returns(Session(101, TestType.Seek));
        repository.GetTestSessionWithoutSamplesAsync(102).Returns(Session(102, TestType.Seek));
        ArrangeEmptySamples(repository);
        ArrangeGenerator(generator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CombinedCertificateExportService(repository, generator)
                .ExportAsync(Card, new[] { 101, 102 }));
        await generator.DidNotReceive().GenerateCombinedPdfAsync(
            Arg.Any<IReadOnlyList<DiskCertificate>>());
    }

    [Fact]
    public async Task WrongCertificateSessionReferenceIsRejected()
    {
        var repository = Substitute.For<IDiskCardRepository>();
        var generator = Substitute.For<ICertificateGenerator>();
        repository.GetTestSessionWithoutSamplesAsync(101).Returns(Session(101, TestType.Seek));
        repository.GetCertificateAsync(55).Returns(new DiskCertificate
        {
            Id = 55, DiskCardId = Card.Id, TestSessionId = 999, TestType = "Seek"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CombinedCertificateExportService(repository, generator)
                .ExportAsync(Card, new[] { 101, 102 }, new[] { 55, 56 }));
        await generator.DidNotReceive().GenerateCombinedPdfAsync(
            Arg.Any<IReadOnlyList<DiskCertificate>>());
    }

    private static TestSession Session(int id, TestType type, int diskId = 42) => new()
    {
        Id = id,
        DiskCardId = diskId,
        TestType = type,
        Grade = type == TestType.Seek ? "B" : "A",
        Score = type == TestType.Seek ? 64 : 81,
        Status = TestStatus.Completed,
        StartedAt = DateTime.UtcNow.AddHours(-1),
        Duration = TimeSpan.FromMinutes(10)
    };

    private static void ArrangeEmptySamples(IDiskCardRepository repository)
    {
        repository.GetSeekSamplesDownsampledAsync(Arg.Any<int>(), 512, Arg.Any<SeekTestType?>())
            .Returns(new List<SeekSampleRecord>());

        repository.GetSpeedSampleSeriesDownsampledAsync(
                Arg.Any<int>(), 512, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((
                new List<SpeedSample>(),
                new List<SpeedSample>())));
    }

    private static void ArrangeGenerator(ICertificateGenerator generator)
    {
        generator.GenerateCertificateAsync(Arg.Any<TestSession>(), Arg.Any<DiskCard>())
            .Returns(info =>
            {
                var session = (TestSession)info[0];
                var card = (DiskCard)info[1];
                return Task.FromResult(new DiskCertificate
                {
                    DiskCardId = card.Id,
                    TestSessionId = session.Id,
                    TestType = session.TestType.ToString(),
                    Grade = session.Grade,
                    Score = session.Score,
                    SerialNumber = card.SerialNumber,
                    DiskModel = card.ModelName
                });
            });
    }
}
