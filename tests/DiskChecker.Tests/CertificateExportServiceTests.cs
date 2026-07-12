using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using TestResult = DiskChecker.Core.Models.TestResult;

namespace DiskChecker.Tests;

/// <summary>
/// Integration tests for <see cref="CertificateExportService"/> that verify
/// the memory-efficient certificate export pipeline works end-to-end with a
/// real SQLite database, using the new SQL-level downsampling methods.
/// </summary>
public class CertificateExportServiceTests
{
    private static (CertificateExportService service, DiskCardRepository repo, SqliteConnection connection) CreateService()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new DiskCheckerDbContext(options);
        context.Database.EnsureCreated();
        SchemaCompatibilityPatcher.Apply(context);
        var repo = new DiskCardRepository(context);

        var settings = Substitute.For<ISettingsService>();
        settings.GetCertificatePathAsync().Returns((string?)null);

        var certGenerator = new CertificateGenerator(
            Substitute.For<ILogger<CertificateGenerator>>(),
            settings);

        var exportService = new CertificateExportService(certGenerator, repo);
        return (exportService, repo, connection);
    }

    private static int CreateDiskCardAndSession(DiskCardRepository repo, int speedSampleCount = 0, int tempSampleCount = 0)
    {
        var card = new DiskCard
        {
            ModelName = "Test Export Drive",
            SerialNumber = "EXP001",
            Capacity = 1_000_000_000_000,
            DiskType = "SSD",
            FirmwareVersion = "1.0",
            InterfaceType = "SATA"
        };
        repo.CreateAsync(card).GetAwaiter().GetResult();

        var session = new TestSession
        {
            DiskCardId = card.Id,
            TestType = TestType.Sanitization,
            Duration = TimeSpan.FromHours(2),
            Status = TestStatus.Completed,
            IsDestructive = true,
            Result = TestResult.Pass,
            HealthAssessment = HealthAssessment.Good,
            Grade = "A",
            Score = 92,
            AverageWriteSpeedMBps = 150.0,
            MaxWriteSpeedMBps = 250.0,
            AverageReadSpeedMBps = 180.0,
            MaxReadSpeedMBps = 300.0,
            StartTemperature = 30,
            MaxTemperature = 45,
            SmartBefore = new SmartaData
            {
                DeviceModel = "Test Export Drive",
                SerialNumber = "EXP001",
                IsHealthy = true,
                PowerOnHours = 1000,
                PowerCycleCount = 50
            }
        };

        if (speedSampleCount > 0)
        {
            session.WriteSamples = Enumerable.Range(0, speedSampleCount)
                .Select(i => new SpeedSample
                {
                    ProgressPercent = (double)i / speedSampleCount * 100,
                    SpeedMBps = 100 + (i % 50),
                    Timestamp = new DateTime(2025, 1, 1).AddSeconds(i)
                }).ToList();
            session.ReadSamples = Enumerable.Range(0, speedSampleCount)
                .Select(i => new SpeedSample
                {
                    ProgressPercent = (double)i / speedSampleCount * 100,
                    SpeedMBps = 80 + (i % 40),
                    Timestamp = new DateTime(2025, 1, 1).AddSeconds(i + speedSampleCount)
                }).ToList();
        }

        if (tempSampleCount > 0)
        {
            session.TemperatureSamples = Enumerable.Range(0, tempSampleCount)
                .Select(i => new TemperatureSample
                {
                    TemperatureCelsius = 30 + (i % 10),
                    Timestamp = new DateTime(2025, 1, 1).AddSeconds(i),
                    Phase = "Write",
                    ProgressPercent = (double)i / tempSampleCount * 100
                }).ToList();
        }

        repo.CreateTestSessionAsync(session).GetAwaiter().GetResult();
        return session.Id;
    }

    [Fact]
    public async Task ExportCertificateAsync_WithLargeDataset_SucceedsAndReturnsCertificate()
    {
        var (service, repo, connection) = CreateService();
        await using var _ = connection;

        // Insert a large dataset simulating a sanitization test
        var sessionId = CreateDiskCardAndSession(repo, speedSampleCount: 100_000, tempSampleCount: 10_000);

        var result = await service.ExportCertificateAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, $"Export failed: {result.ErrorMessage}");
        Assert.NotNull(result.Certificate);
        Assert.False(string.IsNullOrWhiteSpace(result.PdfPath));
        Assert.Equal("A", result.Certificate!.Grade);
        Assert.True(result.Certificate.Score >= 90);

        // Verify chart profiles are downsampled (not full 100k samples)
        Assert.True(result.Certificate.WriteProfilePoints.Count <= 32,
            $"WriteProfilePoints should be <= 32, got {result.Certificate.WriteProfilePoints.Count}");
        Assert.True(result.Certificate.ReadProfilePoints.Count <= 32,
            $"ReadProfilePoints should be <= 32, got {result.Certificate.ReadProfilePoints.Count}");

        // Verify aggregate values are preserved (from session aggregates, not from downsampled chart)
        Assert.Equal(150.0, result.Certificate.AvgWriteSpeed);
        Assert.Equal(250.0, result.Certificate.MaxWriteSpeed);
        Assert.Equal(180.0, result.Certificate.AvgReadSpeed);
        Assert.Equal(300.0, result.Certificate.MaxReadSpeed);
    }

    [Fact]
    public async Task ExportCertificateAsync_WithSmallDataset_Succeeds()
    {
        var (service, repo, connection) = CreateService();
        await using var _ = connection;

        var sessionId = CreateDiskCardAndSession(repo, speedSampleCount: 100, tempSampleCount: 50);

        var result = await service.ExportCertificateAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, $"Export failed: {result.ErrorMessage}");
        Assert.NotNull(result.Certificate);
        Assert.False(string.IsNullOrWhiteSpace(result.PdfPath));
    }

    [Fact]
    public async Task ExportCertificateAsync_WithNoSamples_StillGeneratesCertificate()
    {
        var (service, repo, connection) = CreateService();
        await using var _ = connection;

        var sessionId = CreateDiskCardAndSession(repo, speedSampleCount: 0, tempSampleCount: 0);

        var result = await service.ExportCertificateAsync(sessionId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, $"Export failed: {result.ErrorMessage}");
        Assert.NotNull(result.Certificate);
        // Certificate should still have grade from session
        Assert.Equal("A", result.Certificate!.Grade);
    }

    [Fact]
    public async Task ExportCertificateAsync_InvalidSessionId_ReturnsFailure()
    {
        var (service, repo, connection) = CreateService();
        await using var _ = connection;

        var result = await service.ExportCertificateAsync(99999, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ExportCertificateAsync_ReportsProgress()
    {
        var (service, repo, connection) = CreateService();
        await using var _ = connection;

        var sessionId = CreateDiskCardAndSession(repo, speedSampleCount: 5000, tempSampleCount: 1000);

        var progressReports = new List<CertificateExportProgress>();
        var progress = new Progress<CertificateExportProgress>(p => progressReports.Add(p));

        var result = await service.ExportCertificateAsync(sessionId, progress, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(progressReports);
        // Progress should start at 10 and end at 100
        Assert.Equal(10, progressReports[0].ProgressPercent);
        Assert.Equal(100, progressReports[^1].ProgressPercent);
        // Progress should increase monotonically
        for (var i = 1; i < progressReports.Count; i++)
        {
            Assert.True(progressReports[i].ProgressPercent >= progressReports[i - 1].ProgressPercent,
                $"Progress decreased at step {i}: {progressReports[i].ProgressPercent} < {progressReports[i - 1].ProgressPercent}");
        }
    }

    [Fact]
    public async Task ExportCertificateAsync_WithCancellationToken_RespectsCancellation()
    {
        var (service, repo, connection) = CreateService();
        await using var _ = connection;

        var sessionId = CreateDiskCardAndSession(repo, speedSampleCount: 10_000, tempSampleCount: 1_000);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var result = await service.ExportCertificateAsync(sessionId, null, cts.Token);

        // Should fail with cancellation message
        Assert.False(result.IsSuccess);
    }
}