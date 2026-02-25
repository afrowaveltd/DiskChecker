using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.UI.Console;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Tests for LiveSmartDisplay console component.
/// </summary>
public class LiveSmartDisplayTests
{
    private readonly ISmartaProvider _smartaProvider;
    private readonly IQualityCalculator _qualityCalculator;
    private readonly ILogger<SmartCheckService> _logger;

    public LiveSmartDisplayTests()
    {
        _smartaProvider = Substitute.For<ISmartaProvider>();
        _qualityCalculator = Substitute.For<IQualityCalculator>();
        _logger = Substitute.For<ILogger<SmartCheckService>>();
    }

    /// <summary>
    /// Tests that SMART display can be initialized and started.
    /// </summary>
    [Fact]
    public async Task StartMonitoringAsync_LoadsInitialData()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var smartCheckService = new SmartCheckService(_smartaProvider, _qualityCalculator, dbContext, _logger);
        var display = new LiveSmartDisplay(smartCheckService);

        var drive = new CoreDriveInfo
        {
            Path = "C:\\",
            Name = "Test Drive",
            TotalSize = 1_000_000_000,
            FreeSpace = 500_000_000,
            FileSystem = "NTFS"
        };

        var smartaData = new SmartaData
        {
            DeviceModel = "Test SSD",
            SerialNumber = "SN123456",
            Temperature = 42.5,
            PowerOnHours = 5000,
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            UncorrectableErrorCount = 0
        };

        _smartaProvider.GetSmartaDataAsync(drive.Path, Arg.Any<CancellationToken>())
            .Returns(smartaData);

        _qualityCalculator.CalculateQuality(smartaData)
            .Returns(new QualityRating { Grade = QualityGrade.A, Score = 98.0 });

        // Act
        await display.StartMonitoringAsync(drive);

        // Assert
        Assert.NotNull(display.CurrentSmartData);
        Assert.Equal(42.5, display.CurrentSmartData.Temperature);
        Assert.Equal("Test SSD", display.CurrentSmartData.DeviceModel);
    }

    /// <summary>
    /// Tests that SMART data table can be created.
    /// </summary>
    [Fact]
    public async Task CreateSmartDataTable_ReturnsValidTable()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var smartCheckService = new SmartCheckService(_smartaProvider, _qualityCalculator, dbContext, _logger);
        var display = new LiveSmartDisplay(smartCheckService);

        var drive = new CoreDriveInfo
        {
            Path = "C:\\",
            Name = "Test Drive",
            TotalSize = 1_000_000_000,
            FreeSpace = 500_000_000,
            FileSystem = "NTFS"
        };

        var smartaData = new SmartaData
        {
            DeviceModel = "Samsung SSD 970 EVO",
            ModelFamily = "Samsung 970 Series",
            SerialNumber = "S466NX0N123456",
            FirmwareVersion = "2B2QEXE7",
            Temperature = 35.0,
            PowerOnHours = 10000,
            WearLevelingCount = 15,
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            UncorrectableErrorCount = 0
        };

        _smartaProvider.GetSmartaDataAsync(drive.Path, Arg.Any<CancellationToken>())
            .Returns(smartaData);

        _qualityCalculator.CalculateQuality(smartaData)
            .Returns(new QualityRating { Grade = QualityGrade.A, Score = 95.0 });

        await display.StartMonitoringAsync(drive);

        // Act
        var table = display.CreateSmartDataTable();

        // Assert
        Assert.NotNull(table);
        // Table should have columns
        Assert.NotEmpty(table.Columns);
    }

    /// <summary>
    /// Tests compact status string generation.
    /// </summary>
    [Fact]
    public async Task CreateCompactStatus_ReturnsFormattedString()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var smartCheckService = new SmartCheckService(_smartaProvider, _qualityCalculator, dbContext, _logger);
        var display = new LiveSmartDisplay(smartCheckService);

        var drive = new CoreDriveInfo
        {
            Path = "C:\\",
            Name = "Test Drive",
            TotalSize = 1_000_000_000,
            FreeSpace = 500_000_000,
            FileSystem = "NTFS"
        };

        var smartaData = new SmartaData
        {
            DeviceModel = "Test Drive",
            Temperature = 45.0,
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            UncorrectableErrorCount = 0
        };

        _smartaProvider.GetSmartaDataAsync(drive.Path, Arg.Any<CancellationToken>())
            .Returns(smartaData);

        _qualityCalculator.CalculateQuality(smartaData)
            .Returns(new QualityRating { Grade = QualityGrade.B, Score = 85.0 });

        await display.StartMonitoringAsync(drive);

        // Act
        var status = display.CreateCompactStatus();

        // Assert
        Assert.NotNull(status);
        Assert.Contains("SMART", status);
        Assert.Contains("45", status); // Temperature
    }

    /// <summary>
    /// Tests data refresh functionality.
    /// </summary>
    [Fact]
    public async Task RefreshDataAsync_UpdatesSmartData()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var smartCheckService = new SmartCheckService(_smartaProvider, _qualityCalculator, dbContext, _logger);
        var display = new LiveSmartDisplay(smartCheckService);

        var drive = new CoreDriveInfo
        {
            Path = "C:\\",
            Name = "Test Drive",
            TotalSize = 1_000_000_000,
            FreeSpace = 500_000_000,
            FileSystem = "NTFS"
        };

        var initialData = new SmartaData
        {
            DeviceModel = "Test Drive",
            Temperature = 40.0,
            PowerOnHours = 1000
        };

        var updatedData = new SmartaData
        {
            DeviceModel = "Test Drive",
            Temperature = 55.0, // Temperature increased
            PowerOnHours = 1001
        };

        _smartaProvider.GetSmartaDataAsync(drive.Path, Arg.Any<CancellationToken>())
            .Returns(initialData, updatedData);

        _qualityCalculator.CalculateQuality(Arg.Any<SmartaData>())
            .Returns(new QualityRating { Grade = QualityGrade.A, Score = 90.0 });

        await display.StartMonitoringAsync(drive);
        Assert.Equal(40.0, display.CurrentSmartData?.Temperature);

        // Act
        await display.RefreshDataAsync(drive);

        // Assert
        Assert.Equal(55.0, display.CurrentSmartData?.Temperature);
        Assert.Equal(1001, display.CurrentSmartData?.PowerOnHours);
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
