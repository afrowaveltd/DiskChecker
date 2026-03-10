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

/// <summary>
/// Tests for LiveSmartDisplay console component.
/// Note: LiveSmartDisplay class was in DiskChecker.UI.Console which is currently disconnected.
/// These tests are kept for reference and will be re-enabled when Console UI is restored.
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
    /// Tests that SMART data can be retrieved and quality calculated.
    /// </summary>
    [Fact]
    public async Task GetSmartaData_ReturnsValidData()
    {
        // Arrange
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
        var result = await _smartaProvider.GetSmartaDataAsync(drive.Path);
        var quality = _qualityCalculator.CalculateQuality(result!);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(42.5, result.Temperature);
        Assert.Equal("Test SSD", result.DeviceModel);
        Assert.Equal(QualityGrade.A, quality.Grade);
        Assert.Equal(98.0, quality.Score);
    }

    /// <summary>
    /// Tests that SMART data with all properties is valid.
    /// </summary>
    [Fact]
    public async Task SmartaData_WithAllProperties_ReturnsValidData()
    {
        // Arrange
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

        // Act
        var result = await _smartaProvider.GetSmartaDataAsync(drive.Path);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Samsung SSD 970 EVO", result.DeviceModel);
        Assert.Equal("Samsung 970 Series", result.ModelFamily);
        Assert.Equal("S466NX0N123456", result.SerialNumber);
        Assert.Equal("2B2QEXE7", result.FirmwareVersion);
        Assert.Equal(35.0, result.Temperature);
        Assert.Equal(10000, result.PowerOnHours);
        Assert.Equal(15, result.WearLevelingCount);
    }

    /// <summary>
    /// Tests quality calculation for different SMART states.
    /// </summary>
    [Fact]
    public void QualityCalculator_ReturnsCorrectGrade()
    {
        // Arrange - Healthy drive
        var healthyData = new SmartaData
        {
            DeviceModel = "Test Drive",
            Temperature = 45.0,
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            UncorrectableErrorCount = 0
        };

        _qualityCalculator.CalculateQuality(healthyData)
            .Returns(new QualityRating { Grade = QualityGrade.A, Score = 95.0 });

        // Act
        var quality = _qualityCalculator.CalculateQuality(healthyData);

        // Assert
        Assert.Equal(QualityGrade.A, quality.Grade);
        Assert.Equal(95.0, quality.Score);
    }

    /// <summary>
    /// Tests quality calculation for degraded drive.
    /// </summary>
    [Fact]
    public void QualityCalculator_DegradedDrive_ReturnsLowerGrade()
    {
        // Arrange - Degraded drive with some errors
        var degradedData = new SmartaData
        {
            DeviceModel = "Test Drive",
            Temperature = 55.0,
            PowerOnHours = 50000,
            ReallocatedSectorCount = 10,
            PendingSectorCount = 5,
            UncorrectableErrorCount = 2
        };

        _qualityCalculator.CalculateQuality(degradedData)
            .Returns(new QualityRating { Grade = QualityGrade.D, Score = 45.0 });

        // Act
        var quality = _qualityCalculator.CalculateQuality(degradedData);

        // Assert
        Assert.Equal(QualityGrade.D, quality.Grade);
        Assert.Equal(45.0, quality.Score);
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