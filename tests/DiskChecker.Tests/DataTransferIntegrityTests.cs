using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiskChecker.Tests;

public sealed class DataTransferIntegrityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Theory]
    [InlineData(ImportMode.Add)]
    [InlineData(ImportMode.Replace)]
    public async Task HistoricalImportRemapsIdsPreservesDatesAndIsRepeatable(ImportMode mode)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>().UseSqlite(connection).Options;
        await using var context = new DiskCheckerDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        SchemaCompatibilityPatcher.Apply(context);
        var repo = new DiskCardRepository(context);
        var unrelated = await repo.CreateAsync(new DiskCard
        {
            SerialNumber = "UNRELATED123", ModelName = "Existing disk", DevicePath = "/dev/sda"
        });
        var unrelatedTest = await repo.CreateTestSessionAsync(new TestSession
        {
            DiskCardId = unrelated.Id, TestType = TestType.Sanitization
        });

        var historicalDate = new DateTime(2025, 3, 12, 9, 0, 0, DateTimeKind.Utc);
        var guid = Guid.NewGuid();
        var package = new DataExportPackage();
        package.DiskCards.Add(new DiskCard
        {
            Id = 900, SerialNumber = "NOSN-0123456789ABCDEF01234567", ModelName = "Historical disk",
            DevicePath = "/dev/sda", CreatedAt = historicalDate, LastTestedAt = historicalDate,
            TestCount = 1
        });
        package.TestSessions.Add(new TestSession
        {
            Id = 901, DiskCardId = 900, SessionId = guid, StartedAt = historicalDate,
            CompletedAt = historicalDate.AddMinutes(1), TestType = TestType.Sanitization,
            Status = TestStatus.Completed, AverageReadSpeedMBps = 120, CertificateId = 902
        });
        package.Certificates.Add(new DiskCertificate
        {
            Id = 902, DiskCardId = 900, TestSessionId = 901, CertificateNumber = "IMPORT-902",
            GeneratedAt = historicalDate.AddMinutes(2), PdfPath = "/source/report.pdf", PdfGenerated = true,
            Grade = "A", SerialNumber = package.DiskCards[0].SerialNumber
        });
        string folder = Directory.CreateTempSubdirectory("dc-import-").FullName;
        try
        {
            string path = Path.Combine(folder, "history.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(package, JsonOptions), TestContext.Current.CancellationToken);
            var service = new DataExportImportService(repo);
            var first = await service.ImportAsync(path, mode, TestContext.Current.CancellationToken);
            Assert.True(first.Success, first.ErrorMessage);
            Assert.Equal(1, first.TestSessionsImported);
            Assert.Equal(1, first.CertificatesImported);

            var card = Assert.Single(await repo.GetAllAsync(), c => c.SerialNumber == package.DiskCards[0].SerialNumber);
            Assert.NotEqual(900, card.Id);
            Assert.Equal(string.Empty, card.DevicePath);
            Assert.Equal(1, card.TestCount);
            var test = Assert.Single(await repo.GetTestSessionsAsync(card.Id));
            Assert.Equal(guid, test.SessionId);
            Assert.Equal(historicalDate, test.StartedAt);
            Assert.NotEqual(unrelatedTest.Id, test.Id);
            Assert.NotEqual(901, test.Id);
            var cert = Assert.Single(await repo.GetCertificatesAsync(card.Id));
            Assert.Equal(test.Id, cert.TestSessionId);
            Assert.Equal(cert.Id, test.CertificateId);
            Assert.False(cert.PdfGenerated);
            Assert.Null(cert.PdfPath);
            Assert.Empty(await repo.GetCertificatesAsync(unrelated.Id));

            var second = await service.ImportAsync(path, mode, TestContext.Current.CancellationToken);
            Assert.True(second.Success, second.ErrorMessage);
            Assert.Equal(0, second.TestSessionsImported);
            Assert.Equal(0, second.CertificatesImported);
            Assert.Equal(1, second.TestSessionsSkipped);
            Assert.Equal(1, second.CertificatesSkipped);
            Assert.Single(await repo.GetTestSessionsAsync(card.Id));

            var exportPath = Path.Combine(folder, "roundtrip.json");
            await service.ExportAsync(exportPath, ExportScope.SelectedDisks, new() { card.Id }, TestContext.Current.CancellationToken);
            var metadata = await service.PeekMetadataAsync(exportPath, TestContext.Current.CancellationToken);
            Assert.NotNull(metadata);
            Assert.Equal(1, metadata.TestSessionCount);
            Assert.Equal(1, metadata.CertificateCount);
            Assert.Equal(new FileInfo(exportPath).Length, metadata.TotalSizeBytes);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidCrossDiskCertificateIsRejectedBeforeAnyWrites()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = new DiskCheckerDbContext(
            new DbContextOptionsBuilder<DiskCheckerDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        SchemaCompatibilityPatcher.Apply(context);
        var repo = new DiskCardRepository(context);
        var package = new DataExportPackage();
        package.DiskCards.Add(new DiskCard { Id = 1, SerialNumber = "FIRST" });
        package.DiskCards.Add(new DiskCard { Id = 2, SerialNumber = "SECOND" });
        package.TestSessions.Add(new TestSession { Id = 3, DiskCardId = 1, StartedAt = DateTime.UtcNow });
        package.Certificates.Add(new DiskCertificate { Id = 4, DiskCardId = 2, TestSessionId = 3, CertificateNumber = "WRONG" });
        string folder = Directory.CreateTempSubdirectory("dc-invalid-import-").FullName;
        try
        {
            string path = Path.Combine(folder, "invalid.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(package, JsonOptions), TestContext.Current.CancellationToken);
            var result = await new DataExportImportService(repo).ImportAsync(path, ImportMode.Add, TestContext.Current.CancellationToken);
            Assert.False(result.Success);
            Assert.Empty(await repo.GetAllAsync());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task LateDatabaseFailureRollsBackEarlierCardsAndMetadata()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = new DiskCheckerDbContext(
            new DbContextOptionsBuilder<DiskCheckerDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        SchemaCompatibilityPatcher.Apply(context);
        var repo = new DiskCardRepository(context);
        var original = await repo.CreateAsync(new DiskCard { SerialNumber = "ORIGINAL", ModelName = "Original metadata" });
        var existing = await repo.CreateTestSessionAsync(new TestSession { DiskCardId = original.Id });

        var package = new DataExportPackage();
        package.DiskCards.Add(new DiskCard { Id = 100, SerialNumber = original.SerialNumber, ModelName = "Changed metadata" });
        package.DiskCards.Add(new DiskCard { Id = 101, SerialNumber = "NEW-FIRST" });
        package.DiskCards.Add(new DiskCard { Id = 102, SerialNumber = "NEW-SECOND" });
        package.TestSessions.Add(new TestSession { Id = 200, DiskCardId = 101, StartedAt = DateTime.UtcNow });
        package.TestSessions.Add(new TestSession { Id = 201, DiskCardId = 102, StartedAt = DateTime.UtcNow, SessionId = existing.SessionId });

        string folder = Directory.CreateTempSubdirectory("dc-rollback-").FullName;
        try
        {
            string path = Path.Combine(folder, "late-failure.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(package, JsonOptions), TestContext.Current.CancellationToken);
            var result = await new DataExportImportService(repo).ImportAsync(path, ImportMode.Replace, TestContext.Current.CancellationToken);
            Assert.False(result.Success);
            Assert.Equal(0, result.DisksImported);
            Assert.Equal(0, result.TestSessionsImported);
            Assert.Equal(0, result.CertificatesImported);
            var card = Assert.Single(await repo.GetAllAsync());
            Assert.Equal(original.Id, card.Id);
            Assert.Equal("Original metadata", card.ModelName);
            var session = Assert.Single(await repo.GetTestSessionsAsync(card.Id));
            Assert.Equal(existing.SessionId, session.SessionId);
            Assert.Equal(1, card.TestCount);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationBeforeCommitRollsBackSavedRecords()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = new DiskCheckerDbContext(
            new DbContextOptionsBuilder<DiskCheckerDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        SchemaCompatibilityPatcher.Apply(context);
        var repo = new DiskCardRepository(context);
        using var cancellation = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repo.ExecuteHistoricalImportAsync(async () =>
            {
                await repo.ImportDiskCardAsync(new DiskCard { SerialNumber = "CANCELLED" });
                Assert.Single(await repo.GetAllAsync());
                cancellation.Cancel();
            }, cancellation.Token));

        Assert.Empty(await repo.GetAllAsync());
        // A subsequent operation must not flush stale entities from the cancelled import.
        await repo.CreateAsync(new DiskCard { SerialNumber = "AFTER-CANCEL" });
        Assert.Equal("AFTER-CANCEL", Assert.Single(await repo.GetAllAsync()).SerialNumber);
    }

}
