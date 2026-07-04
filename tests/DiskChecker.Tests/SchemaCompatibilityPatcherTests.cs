using System;
using System.Linq;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiskChecker.Tests;

public class SchemaCompatibilityPatcherTests
{
    [Fact]
    public void Apply_AddsIsStalledToExistingSpeedSampleTables()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE TestSessions (Id INTEGER NOT NULL PRIMARY KEY);
                CREATE TABLE TestSessions_WriteSamples (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TestSessionId INTEGER NOT NULL,
                    Timestamp TEXT NOT NULL,
                    SpeedMBps REAL NOT NULL,
                    ProgressPercent REAL NOT NULL,
                    BytesProcessed INTEGER NOT NULL
                );
                CREATE TABLE TestSessions_ReadSamples (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TestSessionId INTEGER NOT NULL,
                    Timestamp TEXT NOT NULL,
                    SpeedMBps REAL NOT NULL,
                    ProgressPercent REAL NOT NULL,
                    BytesProcessed INTEGER NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var context = new DiskCheckerDbContext(options))
        {
            SchemaCompatibilityPatcher.Apply(context);
        }

        using var verifyCommand = connection.CreateCommand();
        verifyCommand.CommandText = """
            SELECT COUNT(*)
            FROM pragma_table_info('TestSessions_WriteSamples')
            WHERE name = 'IsStalled'
            UNION ALL
            SELECT COUNT(*)
            FROM pragma_table_info('TestSessions_ReadSamples')
            WHERE name = 'IsStalled';
            """;

        using var reader = verifyCommand.ExecuteReader();
        var counts = new System.Collections.Generic.List<long>();
        while (reader.Read())
        {
            counts.Add(reader.GetInt64(0));
        }

        Assert.Equal(new long[] { 1, 1 }, counts);
    }

    [Fact]
    public async Task RepositorySpeedSampleLoad_WorksWithoutIsStalledColumn()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE TestSessions_WriteSamples (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TestSessionId INTEGER NOT NULL,
                    ProgressPercent REAL NOT NULL,
                    SpeedMBps REAL NOT NULL
                );
                CREATE TABLE TestSessions_ReadSamples (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TestSessionId INTEGER NOT NULL,
                    ProgressPercent REAL NOT NULL,
                    SpeedMBps REAL NOT NULL
                );
                INSERT INTO TestSessions_WriteSamples (TestSessionId, ProgressPercent, SpeedMBps) VALUES (42, 10, 123.4);
                INSERT INTO TestSessions_ReadSamples (TestSessionId, ProgressPercent, SpeedMBps) VALUES (42, 20, 234.5);
                """;
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new DiskCheckerDbContext(options);
        var repository = new DiskCardRepository(context);

        var (writeSamples, readSamples) = await repository.GetSpeedSampleSeriesAsync(42);

        Assert.Single(writeSamples);
        Assert.Single(readSamples);
        Assert.False(writeSamples.Single().IsStalled);
        Assert.False(readSamples.Single().IsStalled);
    }

    [Fact]
    public async Task RepositorySpeedSampleLoad_ReadsIsStalledWhenColumnExists()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE TestSessions_WriteSamples (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TestSessionId INTEGER NOT NULL,
                    ProgressPercent REAL NOT NULL,
                    SpeedMBps REAL NOT NULL,
                    IsStalled INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE TestSessions_ReadSamples (
                    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    TestSessionId INTEGER NOT NULL,
                    ProgressPercent REAL NOT NULL,
                    SpeedMBps REAL NOT NULL,
                    IsStalled INTEGER NOT NULL DEFAULT 0
                );
                INSERT INTO TestSessions_WriteSamples (TestSessionId, ProgressPercent, SpeedMBps, IsStalled) VALUES (42, 10, 123.4, 1);
                INSERT INTO TestSessions_ReadSamples (TestSessionId, ProgressPercent, SpeedMBps, IsStalled) VALUES (42, 20, 234.5, 0);
                """;
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new DiskCheckerDbContext(options);
        var repository = new DiskCardRepository(context);

        var (writeSamples, readSamples) = await repository.GetSpeedSampleSeriesAsync(42);

        Assert.True(writeSamples.Single().IsStalled);
        Assert.False(readSamples.Single().IsStalled);
    }
}
