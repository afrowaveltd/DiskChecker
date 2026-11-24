using System;
using System.Linq;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Regression for disk space exhaustion while saving a finished sanitization:
/// disk-card grade queries must never auto-include all owned history samples.
/// A few thousand samples per phase could otherwise cause a giant SQLite
/// cartesian result / temp-file spill just to read ten numeric grades.
/// </summary>
public sealed class SanitizationSaveSpaceRegressionTests
{
    [Fact]
    public async Task RecentCardGrades_GeneratesSqlWithoutOwnedSampleOrErrorJoins()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseSqlite(connection).Options;
        await using var db = new DiskCheckerDbContext(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        db.DiskCards.Add(new DiskCard
        {
            Id = 42,
            ModelName = "Historic 1TB HDD",
            SerialNumber = "SQL-SAMPLE-TEST",
            Capacity = 1_000_000_000_000L
        });

        var session = new TestSession
        {
            DiskCardId = 42,
            TestType = TestType.Sanitization,
            StartedAt = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc),
            Status = TestStatus.Completed,
            Score = 81,
            Grade = "A",
            WriteSamples = Enumerable.Range(0, 150)
                .Select(i => new SpeedSample { SpeedMBps = 100 + i % 10 })
                .ToList(),
            ReadSamples = Enumerable.Range(0, 150)
                .Select(i => new SpeedSample { SpeedMBps = 150 + i % 10 })
                .ToList()
        };
        db.TestSessions.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        // PROVE the prior query shape is dangerous WITHOUT executing it:
        // selecting TestSession entities auto-includes owned collections.
        var formerEntityQuerySql = db.TestSessions
            .AsNoTracking()
            .Where(t => t.DiskCardId == 42)
            .OrderByDescending(t => t.StartedAt)
            .Take(10)
            .ToQueryString();
        Assert.Contains("TestSessions_WriteSamples", formerEntityQuerySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TestSessions_ReadSamples", formerEntityQuerySql, StringComparison.OrdinalIgnoreCase);

        var query = DiskCardTestService.RecentCardGrades(db, 42);
        var sql = query.ToQueryString();

        // The SQL touches ONLY TestSessions scalar columns, regardless of the
        // number or size of stored samples. No joins to owned collections!
        Assert.DoesNotContain("TestSessions_WriteSamples", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TestSessions_ReadSamples", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TestSessions_TemperatureSamples", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TestSessions_SmartChanges", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TestSessions_Errors", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);

        var grades = await query.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(grades);
        Assert.Equal(TestType.Sanitization, grades[0].TestType);
        Assert.Equal(81, grades[0].Score);
        Assert.Equal("A", grades[0].Grade);
        Assert.Empty(db.ChangeTracker.Entries()); // no owned entities materialized/tracked

        // Read/write samples remain exactly where they were, untouched.
        Assert.Equal(150L, await db.Database
            .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM TestSessions_WriteSamples")
            .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(150L, await db.Database
            .SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM TestSessions_ReadSamples")
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecentCardGrades_OnlyReadsMostRecentTenGradesWithoutLoadingHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>().UseSqlite(connection).Options;
        await using var db = new DiskCheckerDbContext(options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        db.DiskCards.Add(new DiskCard { Id = 5, ModelName = "Disk", SerialNumber = "TEN-GRADES" });
        for (int i = 0; i < 12; i++)
        {
            db.TestSessions.Add(new TestSession
            {
                DiskCardId = 5,
                StartedAt = new DateTime(2026, 1, 1).AddDays(i),
                TestType = i % 2 == 0 ? TestType.Sanitization : TestType.Seek,
                Score = i,
                Grade = i == 11 ? "F" : "A"
            });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var selected = await DiskCardTestService.RecentCardGrades(db, 5)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(10, selected.Count);
        Assert.Equal(11, selected[0].Score);
        Assert.Equal(2, selected[^1].Score);
        Assert.Contains(selected, g => g.Grade == "F");
    }
}
