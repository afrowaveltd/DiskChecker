using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DiskChecker.Tui.Tests.Support;

/// <summary>
/// Complete set of disk related test doubles. <see cref="Apply"/> replaces the platform
/// services of the production graph with deterministic fakes, so a screen can be driven
/// end to end without touching real hardware or a database.
/// </summary>
public sealed class DiskFakes
{
    public DiskDetectionFake Detection { get; } = new();

    public SmartaProviderFake Smart { get; } = new();

    public QualityCalculatorFake Quality { get; } = new();

    public SettingsServiceFake Settings { get; } = new();

    public CardRepositoryFake Repository { get; } = new();

    public CertificateGeneratorFake CertificateGenerator { get; } = new();

    /// <summary>Hook passed to <c>TestHarness.Create(configure: fakes.Apply)</c>.</summary>
    public void Apply(IServiceCollection services) => ApplyTo(services);

    public IServiceCollection ApplyTo(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IDiskDetectionService>();
        services.AddSingleton<IDiskDetectionService>(Detection);

        services.RemoveAll<ISmartaProvider>();
        services.AddSingleton<ISmartaProvider>(Smart);

        services.RemoveAll<IQualityCalculator>();
        services.AddSingleton<IQualityCalculator>(Quality);

        services.RemoveAll<ISettingsService>();
        services.AddSingleton<ISettingsService>(Settings);

        services.RemoveAll<IDiskCardRepository>();
        services.AddSingleton<IDiskCardRepository>(Repository);

        services.RemoveAll<ICertificateGenerator>();
        services.AddSingleton<ICertificateGenerator>(CertificateGenerator);

        return services;
    }

    // ------------------------------------------------------------- factories

    /// <summary>Creates a physical drive with realistic identity data.</summary>
    public static CoreDriveInfo CreateDrive(
        string path,
        string name,
        long totalSize,
        bool isSystemDisk = false,
        CoreBusType busType = CoreBusType.Sata,
        bool supportsSmart = true,
        string? serialNumber = null)
    {
        return new CoreDriveInfo
        {
            Path = path,
            Name = name,
            Model = name,
            TotalSize = totalSize,
            SerialNumber = serialNumber,
            IsPhysical = true,
            IsReady = true,
            BusType = busType,
            SupportsSmart = supportsSmart,
            IsSystemDisk = isSystemDisk,
        };
    }

    /// <summary>Creates SMART data whose identity matches the given drive.</summary>
    public static SmartaData CreateSmart(string model, string serialNumber, int temperature = 35, int powerOnHours = 1200)
    {
        return new SmartaData
        {
            DeviceModel = model,
            SerialNumber = serialNumber,
            FirmwareVersion = "FW01",
            Temperature = temperature,
            PowerOnHours = powerOnHours,
            PowerCycleCount = 120,
            IsHealthy = true,
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            UncorrectableErrorCount = 0,
            MediaErrors = 0,
        };
    }

    /// <summary>Creates a stored disk card whose identity key matches the drive.</summary>
    public static DiskCard CreateCard(CoreDriveInfo drive, string? model = null, string grade = "A", double score = 95, bool archived = false)
    {
        string modelName = model ?? drive.Model ?? drive.Name;
        string serial = drive.SerialNumber ?? string.Empty;
        return new DiskCard
        {
            ModelName = modelName,
            Manufacturer = modelName.Split(' ')[0],
            SerialNumber = serial,
            DevicePath = drive.Path,
            Capacity = drive.TotalSize,
            FirmwareVersion = drive.FirmwareVersion ?? "FW01",
            DiskType = "SSD",
            InterfaceType = drive.BusType.ToString(),
            OverallGrade = grade,
            OverallScore = score,
            CreatedAt = new DateTime(2025, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            LastTestedAt = new DateTime(2025, 3, 12, 14, 30, 0, DateTimeKind.Utc),
            TestCount = 1,
            PowerOnHours = 1200,
            PowerCycleCount = 120,
            IsArchived = archived,
            IsLocked = drive.IsSystemDisk,
        };
    }

    /// <summary>Creates a stored test session belonging to the given card.</summary>
    public static TestSession CreateSession(DiskCard card, int id, DateTime startedAt, string grade = "A", double score = 95, string testType = "SurfaceScan")
    {
        var session = new TestSession
        {
            Id = id,
            DiskCardId = card.Id,
            TestType = Enum.TryParse(testType, ignoreCase: true, out TestType parsed) ? parsed : TestType.SurfaceScan,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMinutes(42),
            Duration = TimeSpan.FromMinutes(42),
            Status = TestStatus.Completed,
            Result = TestResult.Pass,
            Grade = grade,
            Score = score,
            BytesRead = 500L * 1024 * 1024 * 1024,
            AverageWriteSpeedMBps = 480,
            AverageReadSpeedMBps = 510,
            StartTemperature = 32,
            MaxTemperature = 41,
            AverageTemperature = 37,
            Notes = "Bez chyb; SMART kontext v normě.",
            SmartBefore = new SmartaData
            {
                DeviceModel = card.ModelName,
                SerialNumber = card.SerialNumber,
                Temperature = 35,
                PowerOnHours = 1200,
                ReallocatedSectorCount = 0,
                PendingSectorCount = 0,
                UncorrectableErrorCount = 0,
                MediaErrors = 0,
            },
        };

        return session;
    }

/// <summary>Deterministic disk detection.</summary>
public sealed class DiskDetectionFake : IDiskDetectionService
{
    public List<CoreDriveInfo> Drives { get; } = new();

    public int GetDrivesCallCount { get; private set; }

    public Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(CancellationToken cancellationToken = default)
    {
        GetDrivesCallCount++;
        IReadOnlyList<CoreDriveInfo> snapshot = Drives.ToList();
        return Task.FromResult(snapshot);
    }

    public Task<CoreDriveInfo?> GetDriveAsync(string path, CancellationToken cancellationToken = default)
    {
        CoreDriveInfo? drive = Drives.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(drive);
    }

    public Task<(int? SpeedMbps, string? Description)> DetectConnectionSpeedAsync(
        string devicePath,
        CoreBusType busType,
        CancellationToken cancellationToken = default)
    {
        (int?, string?) result = (null, null);
        return Task.FromResult(result);
    }
}

/// <summary>Deterministic SMART provider.</summary>
public sealed class SmartaProviderFake : ISmartaProvider
{
    public Dictionary<string, SmartaData> Data { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ProbedPaths { get; } = new();

    public bool LastOperationWasPermissionDenied { get; set; }

    /// <summary>Optional per-path override, e.g. to simulate an exception.</summary>
    public Func<string, SmartaData?>? Handler { get; set; }

    public Task<SmartaData?> GetSmartaDataAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        ProbedPaths.Add(devicePath);
        if (Handler is not null)
        {
            return Task.FromResult(Handler(devicePath));
        }

        return Task.FromResult(Data.TryGetValue(devicePath, out SmartaData? data) ? data : null);
    }

    public Task<bool> IsDriveValidAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Data.ContainsKey(devicePath));

    public Task<List<string>> ListDrivesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Data.Keys.ToList());

    public Task<string> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Empty);

    public Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<int?> GetTemperatureOnlyAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        SmartaData? data = Data.TryGetValue(devicePath, out SmartaData? value) ? value : null;
        return Task.FromResult(data?.Temperature);
    }

    public Task<bool> IsSmartSupportedAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Data.ContainsKey(devicePath));
}

/// <summary>Quality calculator driven by a per-model table.</summary>
public sealed class QualityCalculatorFake : IQualityCalculator
{
    public Dictionary<string, QualityRating?> RatingsByModel { get; } = new(StringComparer.OrdinalIgnoreCase);

    public QualityRating? DefaultRating { get; set; } = new(QualityGrade.A, 95);

    public List<SmartaData> Calls { get; } = new();

    public QualityRating? CalculateQuality(SmartaData? smartaData)
    {
        if (smartaData is null)
        {
            return null;
        }

        Calls.Add(smartaData);
        return RatingsByModel.TryGetValue(smartaData.DeviceModel, out QualityRating? rating)
            ? rating
            : DefaultRating;
    }
}

/// <summary>In-memory settings service with the disk lock list the screens use.</summary>
public sealed class SettingsServiceFake : ISettingsService
{
    public List<string> LockedDisks { get; } = new();

    public List<string> LockCalls { get; } = new();

    public List<string> UnlockCalls { get; } = new();

    public Task<bool> GetAutoCheckForUpdatesAsync() => Task.FromResult(false);

    public Task SetAutoCheckForUpdatesAsync(bool value) => Task.CompletedTask;

    public Task<bool> GetRunAtStartupAsync() => Task.FromResult(false);

    public Task SetRunAtStartupAsync(bool value) => Task.CompletedTask;

    public Task<bool> GetMinimizeToTrayAsync() => Task.FromResult(false);

    public Task SetMinimizeToTrayAsync(bool value) => Task.CompletedTask;

    public Task<int> GetAutoSaveIntervalAsync() => Task.FromResult(60);

    public Task SetAutoSaveIntervalAsync(int value) => Task.CompletedTask;

    public Task<string> GetDefaultExportPathAsync() => Task.FromResult(string.Empty);

    public Task SetDefaultExportPathAsync(string path) => Task.CompletedTask;

    public Task<string> GetLanguageAsync() => Task.FromResult("cs");

    public Task SetLanguageAsync(string language) => Task.CompletedTask;

    public Task<bool> GetEnableLoggingAsync() => Task.FromResult(false);

    public Task SetEnableLoggingAsync(bool value) => Task.CompletedTask;

    public Task<string> GetLogLevelAsync() => Task.FromResult("Warning");

    public Task SetLogLevelAsync(string level) => Task.CompletedTask;

    public Task ResetToDefaultsAsync()
    {
        LockedDisks.Clear();
        return Task.CompletedTask;
    }

    public Task<string> GetReportRecipientEmailAsync() => Task.FromResult(string.Empty);

    public Task SetReportRecipientEmailAsync(string email) => Task.CompletedTask;

    public Task<bool> GetEmailSendOnlyForLongRunningTestsAsync() => Task.FromResult(false);

    public Task SetEmailSendOnlyForLongRunningTestsAsync(bool value) => Task.CompletedTask;

    public Task<bool> GetEmailIncludeCertificateAttachmentAsync() => Task.FromResult(false);

    public Task SetEmailIncludeCertificateAttachmentAsync(bool value) => Task.CompletedTask;

    public Task<bool> GetIsDarkThemeAsync() => Task.FromResult(true);

    public Task SetIsDarkThemeAsync(bool isDark) => Task.CompletedTask;

    public Task<int> GetUsbRecoveryRetryCountAsync() => Task.FromResult(3);

    public Task SetUsbRecoveryRetryCountAsync(int value) => Task.CompletedTask;

    public Task<int> GetSmartCacheTtlMinutesAsync() => Task.FromResult(10);

    public Task SetSmartCacheTtlMinutesAsync(int minutes) => Task.CompletedTask;

    public Task<int> GetSmartProbeTimeoutSecondsAsync() => Task.FromResult(20);

    public Task SetSmartProbeTimeoutSecondsAsync(int seconds) => Task.CompletedTask;

    public Task<int> GetSmartProbeParallelismAsync() => Task.FromResult(4);

    public Task SetSmartProbeParallelismAsync(int parallelism) => Task.CompletedTask;

    public Task SetSmartCacheTtlMinutesAsyncPersistent(int minutes) => Task.CompletedTask;

    public Task<List<string>> GetLockedDisksAsync() => Task.FromResult(LockedDisks.ToList());

    public Task SetLockedDisksAsync(List<string> lockedPaths)
    {
        ArgumentNullException.ThrowIfNull(lockedPaths);
        LockedDisks.Clear();
        LockedDisks.AddRange(lockedPaths);
        return Task.CompletedTask;
    }

    public Task<bool> IsDiskLockedAsync(string diskPath) =>
        Task.FromResult(LockedDisks.Any(candidate => string.Equals(candidate, diskPath, StringComparison.OrdinalIgnoreCase)));

    public Task LockDiskAsync(string diskPath)
    {
        LockCalls.Add(diskPath);
        if (!LockedDisks.Any(candidate => string.Equals(candidate, diskPath, StringComparison.OrdinalIgnoreCase)))
        {
            LockedDisks.Add(diskPath);
        }

        return Task.CompletedTask;
    }

    public Task UnlockDiskAsync(string diskPath)
    {
        UnlockCalls.Add(diskPath);
        LockedDisks.RemoveAll(candidate => string.Equals(candidate, diskPath, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public Task<string?> GetCertificatePathAsync() => Task.FromResult<string?>(null);

    public Task SetCertificatePathAsync(string? path) => Task.CompletedTask;

    public Task<DatabaseStorageSettings> GetDatabaseStorageSettingsAsync() =>
        Task.FromResult(DatabaseStorageSettings.Default);

    public Task SetDatabaseStorageSettingsAsync(DatabaseStorageSettings settings) => Task.CompletedTask;

    public Task<AnalysisWorkspaceMode> GetAnalysisWorkspaceModeAsync() => Task.FromResult(AnalysisWorkspaceMode.Auto);

    public Task SetAnalysisWorkspaceModeAsync(AnalysisWorkspaceMode mode) => Task.CompletedTask;
}

/// <summary>In-memory disk card repository.</summary>
public sealed class CardRepositoryFake : IDiskCardRepository
{
    public List<DiskCard> Cards { get; } = new();

    public List<TestSession> Sessions { get; } = new();

    public List<DiskCertificate> Certificates { get; } = new();

    public List<string> Archived { get; } = new();

    public List<string> Restored { get; } = new();

    public int MergeCallCount { get; private set; }

    public int UpdateCallCount { get; private set; }

    public int MergeResult { get; set; }

    public Task<DiskCard?> GetByIdAsync(int id) =>
        Task.FromResult(Cards.FirstOrDefault(card => card.Id == id));

    public Task<DiskCard?> GetBySerialNumberAsync(string serialNumber) =>
        Task.FromResult(Cards.FirstOrDefault(card =>
            string.Equals(card.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)));

    public Task<DiskCard?> GetByDevicePathAsync(string devicePath) =>
        Task.FromResult(Cards.FirstOrDefault(card =>
            string.Equals(card.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)));

    public Task<List<DiskCard>> GetAllAsync() => Task.FromResult(Cards.ToList());

    public Task<List<DiskCard>> GetActiveAsync() =>
        Task.FromResult(Cards.Where(card => !card.IsArchived).ToList());

    public Task<List<DiskCard>> GetArchivedAsync() =>
        Task.FromResult(Cards.Where(card => card.IsArchived).ToList());

    public Task<DiskCard> CreateAsync(DiskCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.Id == 0)
        {
            card.Id = Cards.Count == 0 ? 1 : Cards.Max(existing => existing.Id) + 1;
        }

        Cards.Add(card);
        return Task.FromResult(card);
    }

    public Task<DiskCard> UpdateAsync(DiskCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        UpdateCallCount++;
        int index = Cards.FindIndex(existing => existing.Id == card.Id);
        if (index >= 0)
        {
            Cards[index] = card;
        }
        else
        {
            Cards.Add(card);
        }

        return Task.FromResult(card);
    }

    public Task DeleteAsync(int id)
    {
        Cards.RemoveAll(card => card.Id == id);
        return Task.CompletedTask;
    }

    public Task ArchiveAsync(int id, ArchiveReason reason, string? notes = null)
    {
        DiskCard? card = Cards.FirstOrDefault(candidate => candidate.Id == id);
        if (card is not null)
        {
            card.IsArchived = true;
            card.ArchiveReason = reason.ToString();
        }

        Archived.Add(reason.ToString());
        return Task.CompletedTask;
    }

    public Task RestoreAsync(int id)
    {
        DiskCard? card = Cards.FirstOrDefault(candidate => candidate.Id == id);
        if (card is not null)
        {
            card.IsArchived = false;
            card.ArchiveReason = null;
        }

        Restored.Add(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Task.CompletedTask;
    }

    public Task<int> MergeDuplicateCardsAsync()
    {
        MergeCallCount++;
        return Task.FromResult(MergeResult);
    }

    public Task<SmartSnapshotRecord> CreateSmartSnapshotAsync(SmartSnapshotRecord snapshot) => Task.FromResult(snapshot);

    public Task<List<SmartSnapshotRecord>> GetSmartSnapshotsAsync(int diskCardId) =>
        Task.FromResult(new List<SmartSnapshotRecord>());

    public Task<List<SmartSnapshotRecord>> GetSmartSnapshotsInRangeAsync(int diskCardId, DateTime fromUtc, DateTime toUtc) =>
        Task.FromResult(new List<SmartSnapshotRecord>());

    public Task DeleteSmartSnapshotsAsync(int diskCardId) => Task.CompletedTask;

    public Task<TestSession?> GetTestSessionAsync(int sessionId) =>
        Task.FromResult(Sessions.FirstOrDefault(session => session.Id == sessionId));

    public Task<TestSession?> GetTestSessionWithoutSamplesAsync(int sessionId) =>
        Task.FromResult(Sessions.FirstOrDefault(session => session.Id == sessionId));

    public Task<List<TestError>> GetTestErrorsAsync(int sessionId, int maxErrors = 100) =>
        Task.FromResult(new List<TestError>());

    public Task<List<TestSession>> GetTestSessionsAsync(int diskCardId) =>
        Task.FromResult(Sessions.Where(session => session.DiskCardId == diskCardId).ToList());

    public Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> GetSpeedSampleSeriesAsync(int sessionId)
    {
        (List<SpeedSample>, List<SpeedSample>) result = (new List<SpeedSample>(), new List<SpeedSample>());
        return Task.FromResult(result);
    }

    public Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> GetSpeedSampleSeriesChunkAsync(
        int sessionId, int modulo, int remainder)
    {
        (List<SpeedSample>, List<SpeedSample>) result = (new List<SpeedSample>(), new List<SpeedSample>());
        return Task.FromResult(result);
    }

    public Task<List<TemperatureSample>> GetTemperatureSampleSeriesAsync(int sessionId) =>
        Task.FromResult(new List<TemperatureSample>());

    public Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> GetSpeedSampleSeriesDownsampledAsync(
        int sessionId, int maxPoints, CancellationToken cancellationToken = default)
    {
        (List<SpeedSample>, List<SpeedSample>) result = (new List<SpeedSample>(), new List<SpeedSample>());
        return Task.FromResult(result);
    }

    public Task<List<TemperatureSample>> GetTemperatureSampleSeriesDownsampledAsync(
        int sessionId, int maxPoints, CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<TemperatureSample>());

    public Task<(int TotalSamples, int StalledSamples)> GetSpeedSampleStallInfoAsync(
        int sessionId, CancellationToken cancellationToken = default)
    {
        (int, int) result = (0, 0);
        return Task.FromResult(result);
    }

    public Task CreateTelemetrySamplesAsync(
        int sessionId,
        TelemetrySamplePhase phase,
        IReadOnlyCollection<SpeedSample> samples,
        bool replacePhase = true) => Task.CompletedTask;

    public Task<List<TestTelemetrySample>> GetTelemetrySamplesAsync(int sessionId, TelemetrySamplePhase? phase = null) =>
        Task.FromResult(new List<TestTelemetrySample>());

    public Task CreateAnomalyEventsAsync(
        int sessionId,
        IReadOnlyCollection<SpeedAnomaly> anomalies,
        bool replaceExisting = true) => Task.CompletedTask;

    public Task<List<TestAnomalyEvent>> GetAnomalyEventsAsync(int sessionId, TelemetrySamplePhase? phase = null) =>
        Task.FromResult(new List<TestAnomalyEvent>());

    public Task CreateStallEventsAsync(
        int sessionId,
        TelemetrySamplePhase phase,
        IReadOnlyCollection<TestStallEvent> events,
        bool replacePhase = true) => Task.CompletedTask;

    public Task<List<TestStallEvent>> GetStallEventsAsync(int sessionId, TelemetrySamplePhase? phase = null) =>
        Task.FromResult(new List<TestStallEvent>());

    public Task CreateSeekSamplesAsync(
        int sessionId,
        SeekTestType testType,
        IReadOnlyCollection<SeekLatencySample> samples) => Task.CompletedTask;

    public Task<List<SeekSampleRecord>> GetSeekSamplesAsync(int sessionId, SeekTestType? testType = null) =>
        Task.FromResult(new List<SeekSampleRecord>());

    public Task<List<SeekSampleRecord>> GetSeekSamplesDownsampledAsync(
        int sessionId, int maxPoints, SeekTestType? testType = null) =>
        Task.FromResult(new List<SeekSampleRecord>());

    public Task<TestSession> CreateTestSessionAsync(TestSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Sessions.Add(session);
        return Task.FromResult(session);
    }

    public Task<TestSession> UpdateTestSessionAsync(TestSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        int index = Sessions.FindIndex(existing => existing.Id == session.Id);
        if (index >= 0)
        {
            Sessions[index] = session;
        }
        else
        {
            Sessions.Add(session);
        }

        return Task.FromResult(session);
    }

    public Task DeleteTestSessionAsync(int sessionId)
    {
        Sessions.RemoveAll(session => session.Id == sessionId);
        return Task.CompletedTask;
    }

    public Task<DiskCertificate?> GetCertificateAsync(int certificateId) =>
        Task.FromResult(Certificates.FirstOrDefault(certificate => certificate.Id == certificateId));

    public Task<DiskCertificate?> GetLatestCertificateAsync(int diskCardId) =>
        Task.FromResult(Certificates
            .Where(certificate => certificate.DiskCardId == diskCardId)
            .OrderByDescending(certificate => certificate.GeneratedAt)
            .FirstOrDefault());

    public Task<List<DiskCertificate>> GetCertificatesAsync(int diskCardId) =>
        Task.FromResult(Certificates.Where(certificate => certificate.DiskCardId == diskCardId).ToList());

    public Task<DiskCertificate> CreateCertificateAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (certificate.Id == 0)
        {
            certificate.Id = Certificates.Count == 0 ? 1 : Certificates.Max(existing => existing.Id) + 1;
        }

        Certificates.Add(certificate);
        return Task.FromResult(certificate);
    }

    public Task UpdateCertificateAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        int index = Certificates.FindIndex(existing => existing.Id == certificate.Id);
        if (index >= 0)
        {
            Certificates[index] = certificate;
        }

        return Task.CompletedTask;
    }

    public Task<bool> UpdateCertificateTechnicianAssessmentAsync(int certificateId, string? assessment) =>
        Task.FromResult(true);

    public void ClearChangeTracker()
    {
        // Nothing is tracked by the in-memory repository.
    }

    public Task<List<DiskCard>> GetBestDisksAsync(int count = 10) =>
        Task.FromResult(Cards.OrderByDescending(card => card.OverallScore).Take(count).ToList());

    public Task<List<DiskCard>> GetByGradeAsync(string grade) =>
        Task.FromResult(Cards.Where(card => string.Equals(card.OverallGrade, grade, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<Dictionary<string, List<DiskCard>>> GetByHealthStatusAsync()
    {
        var result = new Dictionary<string, List<DiskCard>>(StringComparer.Ordinal)
        {
            ["OK"] = Cards.Where(card => card.OverallGrade is "A" or "B" or "C").ToList(),
            ["Critical"] = Cards.Where(card => card.OverallGrade is "D" or "E" or "F").ToList(),
        };
        return Task.FromResult(result);
    }
}

/// <summary>Certificate generator returning stable, SkiaSharp free results.</summary>
public sealed class CertificateGeneratorFake : ICertificateGenerator
{
    public List<int> GeneratedSessions { get; } = new();

    public Task<DiskCertificate> GenerateCertificateAsync(TestSession session, DiskCard diskCard)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(diskCard);

        GeneratedSessions.Add(session.Id);
        return Task.FromResult(new DiskCertificate
        {
            CertificateNumber = "CERT-TEST-" + session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DiskCardId = diskCard.Id,
            TestSessionId = session.Id,
            GeneratedAt = new DateTime(2025, 4, 1, 9, 0, 0, DateTimeKind.Utc),
            DiskModel = diskCard.ModelName,
            SerialNumber = diskCard.SerialNumber,
            Grade = session.Grade,
            Score = session.Score,
            HealthStatus = "OK",
            TestDuration = session.Duration,
            PdfGenerated = true,
        });
    }

    public Task<string?> GenerateAndStoreChartImageAsync(TestSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> EnsureChartImageAsync(TestSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string> GeneratePdfAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Task.FromResult("/tmp/" + certificate.CertificateNumber + ".pdf");
    }

    public Task<string> GenerateLabelAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Task.FromResult("/tmp/" + certificate.CertificateNumber + "-label.png");
    }

    public Task<byte[]> GeneratePreviewAsync(DiskCertificate certificate) =>
        Task.FromResult(Array.Empty<byte>());

    public (string grade, double score) CalculateGrade(TestSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return (session.Grade, session.Score);
    }
}
}
