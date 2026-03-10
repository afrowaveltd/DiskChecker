using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public class SurfaceTestServiceTests
{
    [Fact]
    public async Task RunAsync_PersistsResultAndAssignsTestId()
    {
        var executor = Substitute.For<ISurfaceTestExecutor>();
        using var dbContext = CreateDbContext();
        var persistence = new SurfaceTestPersistenceService(dbContext);
        var service = new SurfaceTestService(executor, persistence);

        var request = new SurfaceTestRequest
        {
            Drive = new CoreDriveInfo { Path = "/dev/sda", Name = "Disk" },
            Technology = DriveTechnology.Hdd,
            Profile = SurfaceTestProfile.HddFull
        };

        var result = new SurfaceTestResult
        {
            Drive = request.Drive,
            Technology = request.Technology,
            Profile = request.Profile,
            Operation = SurfaceTestOperation.WriteZeroFill,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow,
            TotalBytesTested = 4096,
            AverageSpeedMbps = 100,
            PeakSpeedMbps = 120,
            MinSpeedMbps = 80,
            ErrorCount = 0,
            Samples = new List<SurfaceTestSample>()
        };

        executor.ExecuteAsync(Arg.Any<SurfaceTestRequest>(), Arg.Any<IProgress<SurfaceTestProgress>>(), Arg.Any<CancellationToken>())
            .Returns(result);

        var persisted = await service.RunAsync(request);

        Assert.NotEqual(Guid.Empty, persisted.TestId);
        Assert.Single(dbContext.Tests);
        await executor.Received(1)
            .ExecuteAsync(Arg.Any<SurfaceTestRequest>(), Arg.Any<IProgress<SurfaceTestProgress>>(), Arg.Any<CancellationToken>());
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
