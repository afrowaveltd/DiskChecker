using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Uses only an in-memory EF database and fabricated historical disk identity.
/// No physical disks, partitions, user database, or actual certificates touched.
/// </summary>
public sealed class LongTestRecoveryReconcilerTests
{
    private static string NewDirectory()
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "DiskChecker-Reconcile-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static LongTestRecoveryJournal CreateFailedTerminalRun(string directory)
    {
        var journal = new LongTestRecoveryJournal(new CoreDriveInfo
        {
            Path = "/dev/nonexistent-recovery-test-device",
            Name = "Fictional Drive",
            Model = "Fictional Drive",
            SerialNumber = "RECOVERY-TEST-0001",
            TotalSize = 50_000,
            IsPhysical = true
        }, directoryOverride: directory);
        journal.FinishSanitization(new SanitizationResult
        {
            Success = false,
            ErrorMessage = "Read verify failed",
            Duration = TimeSpan.FromMinutes(1),
            BytesWritten = 50_000,
            BytesRead = 35_000
        }, null, null, null, null);

        // Controlled test aging: the reconciler must NOT race very recent
        // live-process writes on production machines.
        var snapshot = JsonSerializer.Deserialize<LongTestRecoverySnapshot>(
            File.ReadAllText(journal.FilePath))!;
        snapshot.EndedAtUtc = DateTime.UtcNow.AddMinutes(-4);
        snapshot.UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-3);
        File.WriteAllText(journal.FilePath, JsonSerializer.Serialize(snapshot));
        return journal;
    }

    private static (DiskCheckerDbContext Context, LongTestRecoveryReconciler Reconciler)
        Create()
    {
        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var context = new DiskCheckerDbContext(options);
        var generator = Substitute.For<ICertificateGenerator>();
        generator.GenerateAndStoreChartImageAsync(
            Arg.Any<TestSession>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns((string?)null);
        var quality = Substitute.For<IQualityCalculator>();
        var logger = Substitute.For<ILogger<DiskCardTestService>>();
        var cards = new DiskCardTestService(context, quality, logger, generator);
        var repository = Substitute.For<IDiskCardRepository>();
        return (context,
            new LongTestRecoveryReconciler(context, cards, generator, repository));
    }

    [Fact]
    public async Task CompletedFailedTestIsImportedOnceWithoutAnyRawDeviceIO()
    {
        var dir = NewDirectory();
        try
        {
            var journal = CreateFailedTerminalRun(dir);
            var (context, reconciler) = Create();
            await using (context)
            {
                var first = await reconciler.ReconcileAsync(
                    dir, TestContext.Current.CancellationToken);
                Assert.Equal(1, first.Found);
                Assert.Equal(1, first.Imported);
                Assert.Equal(1, first.Cleaned);
                Assert.Equal(0, first.Unresolved);
                var stored = Assert.Single(await context.TestSessions.ToListAsync(
                    TestContext.Current.CancellationToken));
                Assert.Equal(journal.RunId, stored.SessionId);
                Assert.Equal(TestStatus.Failed, stored.Status);
                Assert.Equal(TestType.Sanitization, stored.TestType);
                Assert.False(File.Exists(journal.FilePath));

                var second = await reconciler.ReconcileAsync(
                    dir, TestContext.Current.CancellationToken);
                Assert.Equal(0, second.Found);
                Assert.Single(await context.TestSessions.ToListAsync(
                    TestContext.Current.CancellationToken));
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ExistingMatchingSessionIsNotDuplicatedAndJournalCanBeCleaned()
    {
        var dir = NewDirectory();
        try
        {
            var journal = CreateFailedTerminalRun(dir);
            var (context, reconciler) = Create();
            await using (context)
            {
                var card = new DiskCard
                {
                    SerialNumber = "RECOVERY-TEST-0001",
                    ModelName = "Fictional Drive",
                    Capacity = 50_000
                };
                context.DiskCards.Add(card);
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
                context.TestSessions.Add(new TestSession
                {
                    DiskCardId = card.Id,
                    SessionId = journal.RunId,
                    TestType = TestType.Sanitization,
                    Status = TestStatus.Failed
                });
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);

                var summary = await reconciler.ReconcileAsync(
                    dir, TestContext.Current.CancellationToken);
                Assert.Equal(1, summary.AlreadyInDatabase);
                Assert.Equal(0, summary.Imported);
                Assert.Equal(1, summary.Cleaned);
                Assert.Single(await context.TestSessions.ToListAsync(
                    TestContext.Current.CancellationToken));
                Assert.False(File.Exists(journal.FilePath));
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task InterruptedAndCorruptEvidenceRemainUntouched()
    {
        var dir = NewDirectory();
        try
        {
            var live = new LongTestRecoveryJournal(new CoreDriveInfo
            {
                Path = "/dev/nonexistent-safety-test",
                Name = "Fake",
                SerialNumber = "SAFE-0001",
                TotalSize = 50_000
            }, directoryOverride: dir);
            var corrupt = Path.Combine(dir,
                "run_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(corrupt, "{\"FormatVersion\":1");
            var (context, reconciler) = Create();
            await using (context)
            {
                var result = await reconciler.ReconcileAsync(
                    dir, TestContext.Current.CancellationToken);
                Assert.Equal(2, result.Found);
                Assert.Equal(2, result.Unresolved);
                Assert.Equal(0, result.Imported);
                Assert.True(File.Exists(live.FilePath));
                Assert.True(File.Exists(corrupt));
                Assert.Empty(await context.TestSessions.ToListAsync(
                    TestContext.Current.CancellationToken));
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
