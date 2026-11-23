using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiskChecker.Core.Services;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiskChecker.Tests;

public sealed class SanitizationStoragePreflightTests
{
    [Fact]
    public void TelemetryProbe_DetectsPendingFilesWithoutDeletingOrChangingThem()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "DiskCheckerTelemetryPreflight_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var old = Path.Combine(directory, "pending_" + Guid.NewGuid().ToString("N") + ".dat");
        var recent = Path.Combine(directory, "pending_" + Guid.NewGuid().ToString("N") + ".dat");
        var completed = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            File.WriteAllBytes(old, new byte[31]);
            File.WriteAllBytes(recent, new byte[17]);
            File.WriteAllBytes(completed, new byte[13]);
            var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(old, now.AddDays(-3));
            File.SetLastWriteTimeUtc(recent, now.AddHours(-1));

            var result = DiskTelemetryArchive.InspectStorage(directory, now);

            Assert.True(result.IsWritable, result.Failure);
            Assert.Equal(2, result.PendingCount);
            Assert.Equal(1, result.OlderThanOneDayCount);
            Assert.Equal(48L, result.PendingBytes);
            Assert.True(File.Exists(old));
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(completed));
            Assert.Empty(Directory.EnumerateFiles(directory, ".write_probe_*"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TelemetryProbe_BlocksWhenTargetIsNotADirectory()
    {
        var file = Path.GetTempFileName();
        try
        {
            var result = DiskTelemetryArchive.InspectStorage(file);
            Assert.False(result.IsWritable);
            Assert.NotNull(result.Failure);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task SqliteProbe_WritesToMainDbAndRollsBackWithoutPersistentTable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(),
            "DiskCheckerPreflight_" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var opts = new DbContextOptionsBuilder<DiskCheckerDbContext>()
                .UseSqlite("Data Source=" + dbPath + ";Pooling=False").Options;
            await using var context = new DiskCheckerDbContext(opts);
            var token = TestContext.Current.CancellationToken;
            await context.Database.EnsureCreatedAsync(token);

            var probe = await DatabaseWritePreflight.CheckAsync(context, token);

            Assert.True(probe.IsReady, probe.Detail);
            var count = await context.Database.SqlQueryRaw<long>(
                "SELECT COUNT(*) AS Value FROM sqlite_master " +
                "WHERE type = 'table' AND name = '__diskchecker_write_probe'")
                .SingleAsync(token);
            Assert.Equal(0L, count);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
