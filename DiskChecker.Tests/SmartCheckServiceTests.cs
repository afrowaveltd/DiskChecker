using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public class SmartCheckServiceTests
{
    [Fact]
    public async Task RunAsync_ReturnsNull_WhenSmartaDataMissing()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        smartaProvider.GetSmartaDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SmartaData?)null);

        var qualityCalculator = Substitute.For<IQualityCalculator>();
        var logger = Substitute.For<ILogger<SmartCheckService>>();

        using var dbContext = CreateDbContext();
        var service = new SmartCheckService(smartaProvider, qualityCalculator, dbContext, logger);

        var result = await service.RunAsync(new CoreDriveInfo { Path = "/dev/sda", Name = "Disk" });

        Assert.Null(result);
        Assert.Empty(dbContext.Tests);
    }

    [Fact]
    public async Task RunAsync_PersistsSmartSnapshot()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var smartaData = new SmartaData
        {
            SerialNumber = "SN-01",
            DeviceModel = "Model",
            FirmwareVersion = "FW",
            PowerOnHours = 1200,
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            UncorrectableErrorCount = 0,
            Temperature = 33
        };

        smartaProvider.GetSmartaDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(smartaData);

        var qualityCalculator = Substitute.For<IQualityCalculator>();
        qualityCalculator.CalculateQuality(smartaData)
            .Returns(new QualityRating { Grade = QualityGrade.B, Score = 85 });

        var logger = Substitute.For<ILogger<SmartCheckService>>();

        using var dbContext = CreateDbContext();
        var service = new SmartCheckService(smartaProvider, qualityCalculator, dbContext, logger);

        var drive = new CoreDriveInfo { Path = "/dev/sda", Name = "Disk", TotalSize = 1000 };
        var result = await service.RunAsync(drive);

        Assert.NotNull(result);
        Assert.Single(dbContext.Drives);
        Assert.Single(dbContext.Tests);
        Assert.Single(dbContext.SmartaData);

        var testRecord = await dbContext.Tests.SingleAsync();
        Assert.Equal("SmartCheck", testRecord.TestType);
        Assert.Equal(QualityGrade.B, testRecord.Grade);
        Assert.Equal(85, testRecord.Score);
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
