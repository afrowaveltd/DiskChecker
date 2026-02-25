using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Tests for SurfaceTest.razor SMART data display functionality.
/// </summary>
public class SurfaceTestSmartDisplayTests
{
    private readonly ISmartaProvider _smartaProvider;
    private readonly IQualityCalculator _qualityCalculator;
    private readonly ILogger<SmartCheckService> _logger;

    public SurfaceTestSmartDisplayTests()
    {
        _smartaProvider = Substitute.For<ISmartaProvider>();
        _qualityCalculator = Substitute.For<IQualityCalculator>();
        _logger = Substitute.For<ILogger<SmartCheckService>>();
    }

    /// <summary>
    /// Tests that SMART data is loaded before test starts.
    /// </summary>
    [Fact]
    public async Task SmartData_LoadedBeforeTestStart()
    {
        // Arrange
        using var dbContext = CreateDbContext();
        var smartCheckService = new SmartCheckService(_smartaProvider, _qualityCalculator, dbContext, _logger);

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
            Temperature = 45.0,
            PowerOnHours = 1000,
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            UncorrectableErrorCount = 0
        };

        _smartaProvider.GetSmartaDataAsync(drive.Path, Arg.Any<CancellationToken>())
            .Returns(smartaData);

        _qualityCalculator.CalculateQuality(smartaData)
            .Returns(new QualityRating { Grade = QualityGrade.A, Score = 95.0 });

        // Act
        var result = await smartCheckService.RunAsync(drive);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.SmartaData);
        Assert.Equal("Test SSD", result.SmartaData.DeviceModel);
        Assert.Equal(45.0, result.SmartaData.Temperature);
    }

    /// <summary>
    /// Tests temperature class calculation for normal temperature.
    /// </summary>
    [Fact]
    public void GetTemperatureClass_NormalTemperature_ReturnsEmpty()
    {
        // Arrange
        var temperature = 40.0;

        // Act
        var cssClass = GetTemperatureClassHelper(temperature);

        // Assert
        Assert.Equal(string.Empty, cssClass);
    }

    /// <summary>
    /// Tests temperature class calculation for warning temperature.
    /// </summary>
    [Fact]
    public void GetTemperatureClass_WarningTemperature_ReturnsWarning()
    {
        // Arrange
        var temperature = 55.0;

        // Act
        var cssClass = GetTemperatureClassHelper(temperature);

        // Assert
        Assert.Equal("warning", cssClass);
    }

    /// <summary>
    /// Tests temperature class calculation for critical temperature.
    /// </summary>
    [Fact]
    public void GetTemperatureClass_CriticalTemperature_ReturnsError()
    {
        // Arrange
        var temperature = 65.0;

        // Act
        var cssClass = GetTemperatureClassHelper(temperature);

        // Assert
        Assert.Equal("error", cssClass);
    }

    /// <summary>
    /// Tests sector class calculation for no errors.
    /// </summary>
    [Fact]
    public void GetSectorClass_NoErrors_ReturnsEmpty()
    {
        // Arrange
        var count = 0L;

        // Act
        var cssClass = GetSectorClassHelper(count);

        // Assert
        Assert.Equal(string.Empty, cssClass);
    }

    /// <summary>
    /// Tests sector class calculation for few errors.
    /// </summary>
    [Fact]
    public void GetSectorClass_FewErrors_ReturnsWarning()
    {
        // Arrange
        var count = 5L;

        // Act
        var cssClass = GetSectorClassHelper(count);

        // Assert
        Assert.Equal("warning", cssClass);
    }

    /// <summary>
    /// Tests sector class calculation for many errors.
    /// </summary>
    [Fact]
    public void GetSectorClass_ManyErrors_ReturnsError()
    {
        // Arrange
        var count = 50L;

        // Act
        var cssClass = GetSectorClassHelper(count);

        // Assert
        Assert.Equal("error", cssClass);
    }

    /// <summary>
    /// Tests power-on time formatting for hours.
    /// </summary>
    [Fact]
    public void FormatPowerOnTime_Hours_FormatsCorrectly()
    {
        // Arrange
        var hours = 12;

        // Act
        var formatted = FormatPowerOnTimeHelper(hours);

        // Assert
        Assert.Equal("12 h", formatted);
    }

    /// <summary>
    /// Tests power-on time formatting for days.
    /// </summary>
    [Fact]
    public void FormatPowerOnTime_Days_FormatsCorrectly()
    {
        // Arrange
        var hours = 120; // 5 days

        // Act
        var formatted = FormatPowerOnTimeHelper(hours);

        // Assert
        Assert.Equal("5 dní", formatted);
    }

    /// <summary>
    /// Tests power-on time formatting for months.
    /// </summary>
    [Fact]
    public void FormatPowerOnTime_Months_FormatsCorrectly()
    {
        // Arrange
        var hours = 2160; // 90 days = 3 months

        // Act
        var formatted = FormatPowerOnTimeHelper(hours);

        // Assert
        Assert.Equal("3 měsíců", formatted);
    }

    /// <summary>
    /// Tests power-on time formatting for years.
    /// </summary>
    [Fact]
    public void FormatPowerOnTime_Years_FormatsCorrectly()
    {
        // Arrange
        var hours = 26280; // ~3 years

        // Act
        var formatted = FormatPowerOnTimeHelper(hours);

        // Assert
        Assert.Contains("let", formatted);
    }

    // Helper methods that replicate the logic from SurfaceTest.razor
    private static string GetTemperatureClassHelper(double temperature)
    {
        if (temperature >= 60)
            return "error";

        if (temperature >= 50)
            return "warning";

        return string.Empty;
    }

    private static string GetSectorClassHelper(long count)
    {
        if (count > 10)
            return "error";

        if (count > 0)
            return "warning";

        return string.Empty;
    }

    private static string FormatPowerOnTimeHelper(int hours)
    {
        if (hours < 24)
            return $"{hours} h";

        var days = hours / 24;
        if (days < 30)
            return $"{days} dní";

        var months = days / 30;
        if (months < 12)
            return $"{months} měsíců";

        var years = months / 12;
        var remainingMonths = months % 12;
        return remainingMonths > 0 ? $"{years} let {remainingMonths} měsíců" : $"{years} let";
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
