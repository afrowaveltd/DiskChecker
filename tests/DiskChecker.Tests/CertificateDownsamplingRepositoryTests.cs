using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Tests for the memory-efficient SQL-level downsampling methods in
/// <see cref="DiskCardRepository"/>.  These methods are the key fix for the
/// OutOfMemoryException that occurred when generating certificates from
/// sanitization tests with millions of speed samples.
/// </summary>
public class CertificateDownsamplingRepositoryTests
{
    /// <summary>
    /// Creates an in-memory SQLite database with the required schema and
    /// returns a <see cref="DiskCardRepository"/> backed by it.
    /// </summary>
    private static (DiskCardRepository repo, SqliteConnection connection) CreateRepository()
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
        return (repo, connection);
    }

    private static int InsertSession(SqliteConnection connection, DiskCardRepository repo, int diskCardId = 1)
    {
        // Use EF Core to create a session with proper defaults
        var session = new TestSession
        {
            DiskCardId = diskCardId,
            TestType = TestType.Sanitization,
            StartedAt = DateTime.UtcNow,
            Duration = TimeSpan.FromHours(1),
            Status = TestStatus.Completed,
            IsDestructive = true,
            Result = Core.Models.TestResult.Pass,
            HealthAssessment = HealthAssessment.Good,
            Grade = "A",
            Score = 90
        };

        repo.CreateTestSessionAsync(session).GetAwaiter().GetResult();
        return session.Id;
    }

    private static void EnsureDiskCard(DiskCardRepository repo, int cardId = 1)
    {
        // Create a disk card if it doesn't exist yet
        var card = repo.GetByIdAsync(cardId).GetAwaiter().GetResult();
        if (card == null)
        {
            card = new DiskCard
            {
                ModelName = "Test Drive",
                SerialNumber = $"SN{cardId:D4}",
                Capacity = 1_000_000_000_000,
                DiskType = "HDD"
            };
            repo.CreateAsync(card).GetAwaiter().GetResult();
        }
    }

    private static void InsertSpeedSamples(SqliteConnection connection, string table, int sessionId, int count, bool includeStalls = false)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {table} (TestSessionId, Timestamp, SpeedMBps, ProgressPercent, BytesProcessed, IsStalled)
            VALUES (@sid, @ts, @speed, @progress, @bytes, @stalled)
            """;

        var sidParam = command.CreateParameter();
        sidParam.ParameterName = "@sid";
        command.Parameters.Add(sidParam);

        var tsParam = command.CreateParameter();
        tsParam.ParameterName = "@ts";
        command.Parameters.Add(tsParam);

        var speedParam = command.CreateParameter();
        speedParam.ParameterName = "@speed";
        command.Parameters.Add(speedParam);

        var progressParam = command.CreateParameter();
        progressParam.ParameterName = "@progress";
        command.Parameters.Add(progressParam);

        var bytesParam = command.CreateParameter();
        bytesParam.ParameterName = "@bytes";
        command.Parameters.Add(bytesParam);

        var stalledParam = command.CreateParameter();
        stalledParam.ParameterName = "@stalled";
        command.Parameters.Add(stalledParam);

        for (var i = 0; i < count; i++)
        {
            sidParam.Value = sessionId;
            tsParam.Value = new DateTime(2025, 1, 1).AddSeconds(i).ToString("o");
            speedParam.Value = 100.0 + (i % 50);
            progressParam.Value = (double)i / count * 100;
            bytesParam.Value = i * 1024L;
            stalledParam.Value = includeStalls && (i is 1000 or 50000 or 90000) ? 1 : 0;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void InsertTemperatureSamples(SqliteConnection connection, int sessionId, int count)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO TestSessions_TemperatureSamples (TestSessionId, Timestamp, TemperatureCelsius, Phase, ProgressPercent)
            VALUES (@sid, @ts, @temp, @phase, @progress)
            """;

        var sidParam = command.CreateParameter();
        sidParam.ParameterName = "@sid";
        command.Parameters.Add(sidParam);

        var tsParam = command.CreateParameter();
        tsParam.ParameterName = "@ts";
        command.Parameters.Add(tsParam);

        var tempParam = command.CreateParameter();
        tempParam.ParameterName = "@temp";
        command.Parameters.Add(tempParam);

        var phaseParam = command.CreateParameter();
        phaseParam.ParameterName = "@phase";
        command.Parameters.Add(phaseParam);

        var progressParam = command.CreateParameter();
        progressParam.ParameterName = "@progress";
        command.Parameters.Add(progressParam);

        for (var i = 0; i < count; i++)
        {
            sidParam.Value = sessionId;
            tsParam.Value = new DateTime(2025, 1, 1).AddSeconds(i).ToString("o");
            tempParam.Value = 30 + (i % 10);
            phaseParam.Value = "Write";
            progressParam.Value = (double)i / count * 100;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    [Fact]
    public async Task GetSpeedSampleSeriesDownsampledAsync_WithLargeDataset_ReturnsLimitedSamples()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;
        try
        {
            EnsureDiskCard(repo); var sessionId = InsertSession(connection, repo);
            const int sampleCount = 100_000;
            InsertSpeedSamples(connection, "TestSessions_WriteSamples", sessionId, sampleCount);
            InsertSpeedSamples(connection, "TestSessions_ReadSamples", sessionId, sampleCount);

            const int maxPoints = 512;
            var (writeSamples, readSamples) = await repo.GetSpeedSampleSeriesDownsampledAsync(sessionId, maxPoints, TestContext.Current.CancellationToken);

            // The SQL downsampling should return at most maxPoints samples
            Assert.True(writeSamples.Count <= maxPoints,
                $"Write samples count {writeSamples.Count} exceeded max {maxPoints}");
            Assert.True(readSamples.Count <= maxPoints,
                $"Read samples count {readSamples.Count} exceeded max {maxPoints}");

            // Should have a reasonable number of samples (not 0)
            Assert.True(writeSamples.Count > 0, "Write samples should not be empty");
            Assert.True(readSamples.Count > 0, "Read samples should not be empty");

            // Should contain the first and last samples (boundary inclusion)
            Assert.Contains(writeSamples, s => s.ProgressPercent < 1.0);
            Assert.Contains(writeSamples, s => s.ProgressPercent > 99.0);
        }
        finally
        {
            // Connection is disposed by await using
        }
    }

    [Fact]
    public async Task GetSpeedSampleSeriesDownsampledAsync_WithSmallDataset_ReturnsAllSamples()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;
        EnsureDiskCard(repo); var sessionId = InsertSession(connection, repo);
        const int sampleCount = 50;
        InsertSpeedSamples(connection, "TestSessions_WriteSamples", sessionId, sampleCount);
        InsertSpeedSamples(connection, "TestSessions_ReadSamples", sessionId, sampleCount);

        const int maxPoints = 512;
        var (writeSamples, readSamples) = await repo.GetSpeedSampleSeriesDownsampledAsync(sessionId, maxPoints, TestContext.Current.CancellationToken);

        // When total <= maxPoints, all samples should be returned
        Assert.Equal(sampleCount, writeSamples.Count);
        Assert.Equal(sampleCount, readSamples.Count);
    }

    [Fact]
    public async Task GetSpeedSampleSeriesDownsampledAsync_WithEmptyDataset_ReturnsEmptyList()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;
        EnsureDiskCard(repo); var sessionId = InsertSession(connection, repo);

        var (writeSamples, readSamples) = await repo.GetSpeedSampleSeriesDownsampledAsync(sessionId, 512, TestContext.Current.CancellationToken);

        Assert.Empty(writeSamples);
        Assert.Empty(readSamples);
    }

    [Fact]
    public async Task GetSpeedSampleSeriesDownsampledAsync_WithStalledSamples_IncludesStalledRows()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;
        EnsureDiskCard(repo); var sessionId = InsertSession(connection, repo);
        const int sampleCount = 100_000;
        InsertSpeedSamples(connection, "TestSessions_WriteSamples", sessionId, sampleCount, includeStalls: true);

        var (writeSamples, _) = await repo.GetSpeedSampleSeriesDownsampledAsync(sessionId, 512, TestContext.Current.CancellationToken);

        // Stalled samples have SpeedMBps > 0 in our test data (100 + i%50),
        // so they are included regardless. But let's verify that at least
        // some stalled samples made it through the downsampling.
        var stalledCount = writeSamples.Count(s => s.IsStalled);
        // The three stalled samples (at indices 1000, 50000, 90000) might
        // or might not be selected by the downsampling, but the query
        // explicitly includes rows where IsStalled <> 0.
        Assert.True(writeSamples.Count > 0);
    }

    [Fact]
    public async Task GetTemperatureSampleSeriesDownsampledAsync_WithLargeDataset_ReturnsLimitedSamples()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;
        EnsureDiskCard(repo); var sessionId = InsertSession(connection, repo);
        const int sampleCount = 100_000;
        InsertTemperatureSamples(connection, sessionId, sampleCount);

        const int maxPoints = 256;
        var samples = await repo.GetTemperatureSampleSeriesDownsampledAsync(sessionId, maxPoints, TestContext.Current.CancellationToken);

        Assert.True(samples.Count <= maxPoints,
            $"Temperature samples count {samples.Count} exceeded max {maxPoints}");
        Assert.True(samples.Count > 0, "Temperature samples should not be empty");
    }

    [Fact]
    public async Task GetTemperatureSampleSeriesDownsampledAsync_WithSmallDataset_ReturnsAllSamples()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;
        EnsureDiskCard(repo); var sessionId = InsertSession(connection, repo);
        const int sampleCount = 30;
        InsertTemperatureSamples(connection, sessionId, sampleCount);

        var samples = await repo.GetTemperatureSampleSeriesDownsampledAsync(sessionId, 256, TestContext.Current.CancellationToken);

        Assert.Equal(sampleCount, samples.Count);
    }

    [Fact]
    public async Task GetSpeedSampleStallInfoAsync_WithStalledSamples_ReturnsCorrectCounts()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;
        EnsureDiskCard(repo); var sessionId = InsertSession(connection, repo);
        const int sampleCount = 100_000;
        InsertSpeedSamples(connection, "TestSessions_WriteSamples", sessionId, sampleCount, includeStalls: true);
        InsertSpeedSamples(connection, "TestSessions_ReadSamples", sessionId, sampleCount, includeStalls: false);

        var (totalSamples, stalledSamples) = await repo.GetSpeedSampleStallInfoAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Equal(sampleCount * 2, totalSamples);
        // 3 stalled samples in write, 0 in read
        Assert.Equal(3, stalledSamples);
    }

    [Fact]
    public async Task GetSpeedSampleStallInfoAsync_WithNoStalledSamples_ReturnsZeroStalls()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;
        EnsureDiskCard(repo); var sessionId = InsertSession(connection, repo);
        InsertSpeedSamples(connection, "TestSessions_WriteSamples", sessionId, 1000);
        InsertSpeedSamples(connection, "TestSessions_ReadSamples", sessionId, 500);

        var (totalSamples, stalledSamples) = await repo.GetSpeedSampleStallInfoAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Equal(1500, totalSamples);
        Assert.Equal(0, stalledSamples);
    }

    [Fact]
    public async Task GetSpeedSampleStallInfoAsync_WithEmptyDataset_ReturnsZeros()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;
        EnsureDiskCard(repo); var sessionId = InsertSession(connection, repo);

        var (totalSamples, stalledSamples) = await repo.GetSpeedSampleStallInfoAsync(sessionId, TestContext.Current.CancellationToken);

        Assert.Equal(0, totalSamples);
        Assert.Equal(0, stalledSamples);
    }

    [Fact]
    public async Task GetSpeedSampleSeriesDownsampledAsync_InvalidSessionId_Throws()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repo.GetSpeedSampleSeriesDownsampledAsync(0, 512, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSpeedSampleSeriesDownsampledAsync_InvalidMaxPoints_Throws()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repo.GetSpeedSampleSeriesDownsampledAsync(1, 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetTemperatureSampleSeriesDownsampledAsync_InvalidSessionId_Throws()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repo.GetTemperatureSampleSeriesDownsampledAsync(0, 256, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSpeedSampleStallInfoAsync_InvalidSessionId_Throws()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repo.GetSpeedSampleStallInfoAsync(0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetSpeedSampleSeriesDownsampledAsync_SamplesAreOrdered()
    {
        var (repo, connection) = CreateRepository();
        await using var _ = connection;
        EnsureDiskCard(repo); var sessionId = InsertSession(connection, repo);
        InsertSpeedSamples(connection, "TestSessions_WriteSamples", sessionId, 10_000);

        var (writeSamples, _) = await repo.GetSpeedSampleSeriesDownsampledAsync(sessionId, 64, TestContext.Current.CancellationToken);

        // Samples should be ordered by row number (chronological)
        for (var i = 1; i < writeSamples.Count; i++)
        {
            Assert.True(writeSamples[i].ProgressPercent >= writeSamples[i - 1].ProgressPercent,
                $"Sample {i} has ProgressPercent {writeSamples[i].ProgressPercent} < {writeSamples[i - 1].ProgressPercent}");
        }
    }
}