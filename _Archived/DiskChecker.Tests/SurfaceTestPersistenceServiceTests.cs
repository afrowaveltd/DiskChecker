using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiskChecker.Tests;

public class SurfaceTestPersistenceServiceTests
{
    [Fact]
    public async Task SaveAsync_PersistsSurfaceTestSamples()
    {
        using var dbContext = CreateDbContext();
        var service = new SurfaceTestPersistenceService(dbContext);

        var result = new SurfaceTestResult
        {
            Drive = new CoreDriveInfo { Path = "/dev/sda", Name = "Disk", TotalSize = 1000 },
            Technology = DriveTechnology.Hdd,
            Profile = SurfaceTestProfile.HddFull,
            Operation = SurfaceTestOperation.WriteZeroFill,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            CompletedAtUtc = DateTime.UtcNow,
            TotalBytesTested = 2048,
            AverageSpeedMbps = 120,
            PeakSpeedMbps = 150,
            MinSpeedMbps = 90,
            ErrorCount = 0,
            Samples = new List<SurfaceTestSample>
            {
                new() { OffsetBytes = 0, BlockSizeBytes = 1024, ThroughputMbps = 120, TimestampUtc = DateTime.UtcNow, ErrorCount = 0 },
                new() { OffsetBytes = 1024, BlockSizeBytes = 1024, ThroughputMbps = 118, TimestampUtc = DateTime.UtcNow, ErrorCount = 0 }
            }
        };

        var testId = await service.SaveAsync(result);

        Assert.NotEqual(Guid.Empty, testId);
        Assert.Single(dbContext.Tests);
        Assert.Single(dbContext.Drives);
        Assert.Equal(2, dbContext.SurfaceTestSamples.Count());
    }

    private static DiskCheckerDbContext CreateDbContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new DiskCheckerDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
