using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using NSubstitute;

namespace DiskChecker.Tui.Tests.Support;

/// <summary>
/// Deterministic builders and fakes for the data and report screens: disk cards
/// with their test sessions and certificates, an in-memory certificate generator
/// and throwaway export directories.
/// </summary>
public static class DataFakes
{
    /// <summary>Fixed timestamp every fixture is derived from.</summary>
    public static readonly DateTime BaseDate = new(2024, 9, 12, 8, 30, 0, DateTimeKind.Utc);

    public static CardFixture Card(
        int id = 1,
        string model = "Samsung 870 EVO",
        string serial = "S6PENX0T123456",
        string devicePath = "/dev/sda",
        long capacity = 500_107_862_016)
    {
        return new CardFixture(new DiskCard
        {
            Id = id,
            ModelName = model,
            Manufacturer = "Samsung",
            SerialNumber = serial,
            DevicePath = devicePath,
            Capacity = capacity,
            DiskType = "SSD",
            InterfaceType = "SATA",
            FirmwareVersion = "SVT01B6Q",
            ConnectionType = "SATA",
            CreatedAt = BaseDate,
            LastTestedAt = BaseDate,
            OverallGrade = "A",
            OverallScore = 96,
            TestCount = 1,
        });
    }

    public static TestSession Session(
        int id,
        int cardId,
        TestType testType = TestType.FullReadWrite,
        string grade = "A",
        double score = 96,
        DateTime? startedAt = null,
        int errors = 0,
        int? certificateId = null)
    {
        DateTime start = startedAt ?? BaseDate;
        return new TestSession
        {
            Id = id,
            DiskCardId = cardId,
            SessionId = Guid.NewGuid(),
            TestType = testType,
            StartedAt = start,
            CompletedAt = start.AddMinutes(42),
            Duration = TimeSpan.FromMinutes(42),
            Status = TestStatus.Completed,
            Result = errors > 0 ? TestResult.Warning : TestResult.Pass,
            Grade = grade,
            Score = score,
            HealthAssessment = HealthAssessment.Good,
            BytesWritten = 500_000_000_000,
            BytesRead = 500_000_000_000,
            AverageWriteSpeedMBps = 512.5,
            MaxWriteSpeedMBps = 545.2,
            MinWriteSpeedMBps = 480.1,
            AverageReadSpeedMBps = 530.4,
            MaxReadSpeedMBps = 560.9,
            MinReadSpeedMBps = 510.2,
            WriteErrors = errors,
            StartTemperature = 32,
            MaxTemperature = 44,
            AverageTemperature = 38.5,
            CertificateId = certificateId,
        };
    }

    public static DiskCertificate Certificate(
        int id,
        int diskCardId,
        int testSessionId,
        string number = "CERT-2024-0001",
        string model = "Samsung 870 EVO",
        string serial = "S6PENX0T123456",
        string grade = "A",
        double score = 96,
        DateTime? generatedAt = null,
        bool recommended = true,
        string? notes = null)
    {
        return new DiskCertificate
        {
            Id = id,
            CertificateNumber = number,
            DiskCardId = diskCardId,
            TestSessionId = testSessionId,
            GeneratedAt = generatedAt ?? BaseDate.AddDays(1),
            GeneratedBy = "tester",
            DiskModel = model,
            SerialNumber = serial,
            Capacity = "465.8 GB",
            DiskType = "SSD",
            Firmware = "SVT01B6Q",
            Interface = "SATA",
            TestType = "FullReadWrite",
            Grade = grade,
            Score = score,
            HealthStatus = "Dobrý",
            TestDuration = TimeSpan.FromMinutes(42),
            AvgWriteSpeed = 512.5,
            MaxWriteSpeed = 545.2,
            AvgReadSpeed = 530.4,
            MaxReadSpeed = 560.9,
            TemperatureRange = "32–44 °C",
            ErrorCount = 0,
            SmartPassed = true,
            PowerOnHours = 1234,
            PowerCycles = 56,
            ReallocatedSectors = 0,
            PendingSectors = 0,
            Status = CertificateStatus.Active,
            Recommended = recommended,
            RecommendationNotes = "Výborný stav - doporučeno pro všechny účely.",
            Notes = notes ?? "Bez závad.",
        };
    }

    /// <summary>Builds a repository substitute wired to the supplied card fixtures.</summary>
    public static IDiskCardRepository Repository(params CardFixture[] fixtures)
    {
        var repository = Substitute.For<IDiskCardRepository>();
        var cards = fixtures.Select(fixture => fixture.Card).ToList();
        var sessions = fixtures.SelectMany(fixture => fixture.Sessions).ToList();
        var certificates = fixtures.SelectMany(fixture => fixture.Certificates).ToList();

        repository.GetAllAsync().Returns(Task.FromResult(cards));
        repository.GetActiveAsync().Returns(Task.FromResult(cards));
        repository.GetBySerialNumberAsync(Arg.Any<string>()).Returns(call =>
        {
            string serial = call.ArgAt<string>(0);
            return Task.FromResult(cards.FirstOrDefault(card => card.SerialNumber == serial));
        });
        repository.GetByDevicePathAsync(Arg.Any<string>()).Returns(call =>
        {
            string path = call.ArgAt<string>(0);
            return Task.FromResult(cards.FirstOrDefault(card => card.DevicePath == path));
        });
        repository.GetTestSessionAsync(Arg.Any<int>()).Returns(call =>
        {
            int id = call.ArgAt<int>(0);
            return Task.FromResult(sessions.FirstOrDefault(session => session.Id == id));
        });
        repository.GetTestSessionWithoutSamplesAsync(Arg.Any<int>()).Returns(call =>
        {
            int id = call.ArgAt<int>(0);
            return Task.FromResult(sessions.FirstOrDefault(session => session.Id == id));
        });
        repository.GetCertificateAsync(Arg.Any<int>()).Returns(call =>
        {
            int id = call.ArgAt<int>(0);
            return Task.FromResult(certificates.FirstOrDefault(certificate => certificate.Id == id));
        });
        repository.GetLatestCertificateAsync(Arg.Any<int>()).Returns(call =>
        {
            int cardId = call.ArgAt<int>(0);
            return Task.FromResult(certificates
                .Where(certificate => certificate.DiskCardId == cardId)
                .OrderByDescending(certificate => certificate.GeneratedAt)
                .FirstOrDefault());
        });
        repository.DeleteTestSessionAsync(Arg.Any<int>()).Returns(Task.CompletedTask);
        repository.UpdateCertificateAsync(Arg.Any<DiskCertificate>())
            .Returns(call => Task.FromResult(call.Arg<DiskCertificate>()));
        repository.CreateCertificateAsync(Arg.Any<DiskCertificate>())
            .Returns(call => Task.FromResult(call.Arg<DiskCertificate>()));
        repository.UpdateCertificateTechnicianAssessmentAsync(Arg.Any<int>(), Arg.Any<string?>())
            .Returns(Task.FromResult(true));
        repository.GetSpeedSampleSeriesDownsampledAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((new List<SpeedSample>(), new List<SpeedSample>())));
        repository.GetSpeedSampleStallInfoAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((0, 0)));
        repository.GetTemperatureSampleSeriesDownsampledAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<TemperatureSample>()));

        foreach (var fixture in fixtures)
        {
            repository.GetTestSessionsAsync(fixture.Card.Id).Returns(Task.FromResult(fixture.Sessions));
            repository.GetCertificatesAsync(fixture.Card.Id).Returns(Task.FromResult(fixture.Certificates));
            repository.GetByIdAsync(fixture.Card.Id).Returns(Task.FromResult<DiskCard?>(fixture.Card));
        }

        return repository;
    }

    /// <summary>Creates a writable directory used as the default export path.</summary>
    public static string CreateExportDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "dcheck-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temporary directories are best effort cleanup.
        }
    }
}

/// <summary>One disk card together with the sessions and certificates stored for it.</summary>
public sealed class CardFixture
{
    public CardFixture(DiskCard card) => Card = card;

    public DiskCard Card { get; }

    public List<TestSession> Sessions { get; } = new();

    public List<DiskCertificate> Certificates { get; } = new();

    public CardFixture WithSession(TestSession session)
    {
        session.DiskCardId = Card.Id;
        Sessions.Add(session);
        return this;
    }

    public CardFixture WithCertificate(DiskCertificate certificate)
    {
        certificate.DiskCardId = Card.Id;
        Certificates.Add(certificate);
        return this;
    }
}

/// <summary>
/// Certificate generator that never touches SkiaSharp or the file system beyond a
/// throwaway PDF placeholder, so export flows can be asserted deterministically.
/// </summary>
public sealed class StubCertificateGenerator : ICertificateGenerator
{
    public List<DiskCertificate> Generated { get; } = new();

    public int PdfGenerationCount { get; private set; }

    public string? LastPdfPath { get; private set; }

    public Task<DiskCertificate> GenerateCertificateAsync(TestSession session, DiskCard diskCard)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(diskCard);

        var certificate = DataFakes.Certificate(
            id: 0,
            diskCardId: diskCard.Id,
            testSessionId: session.Id,
            number: $"CERT-{diskCard.Id}-{session.Id}",
            model: diskCard.ModelName,
            serial: diskCard.SerialNumber,
            grade: session.Grade,
            score: session.Score);

        Generated.Add(certificate);
        return Task.FromResult(certificate);
    }

    public Task<string?> GenerateAndStoreChartImageAsync(TestSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> EnsureChartImageAsync(TestSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string> GeneratePdfAsync(DiskCertificate certificate)
    {
        PdfGenerationCount++;
        string path = Path.Combine(
            Path.GetTempPath(),
            "dcheck-pdf",
            certificate.CertificateNumber + "-" + Guid.NewGuid().ToString("N") + ".pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "PDF placeholder");
        LastPdfPath = path;
        return Task.FromResult(path);
    }

    public Task<string> GenerateLabelAsync(DiskCertificate certificate) =>
        Task.FromResult(Path.Combine(Path.GetTempPath(), "label.png"));

    public Task<byte[]> GeneratePreviewAsync(DiskCertificate certificate) =>
        Task.FromResult(Array.Empty<byte>());

    public (string grade, double score) CalculateGrade(TestSession session) => ("A", 96);
}
