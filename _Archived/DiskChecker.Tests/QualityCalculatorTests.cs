using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Services;
using DiskChecker.Core.Models;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public class QualityCalculatorTests
{
    private readonly QualityCalculator _calculator;

    public QualityCalculatorTests()
    {
        _calculator = new QualityCalculator();
    }

    [Fact]
    public void CalculateQualityAsGradeWhenNoIssues()
    {
        // Arrange
        var smartaData = new SmartaData
        {
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            PowerOnHours = 500,
            Temperature = 35,
            UncorrectableErrorCount = 0
        };

        // Act
        var result = _calculator.CalculateQuality(smartaData);

        // Assert
        Assert.Equal(QualityGrade.A, result.Grade);
        Assert.True(result.Score >= 90);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void CalculateQualityBGradeWhenSlightIssues()
    {
        // Arrange
        var smartaData = new SmartaData
        {
            ReallocatedSectorCount = 5,
            PendingSectorCount = 2,
            PowerOnHours = 5000,
            Temperature = 45,
            UncorrectableErrorCount = 0
        };

        // Act
        var result = _calculator.CalculateQuality(smartaData);

        // Assert
        Assert.Equal(QualityGrade.B, result.Grade);
        Assert.True(result.Score >= 80);
    }

    [Fact]
    public void CalculateQualityDGradeWhenFailures()
    {
        // Arrange
        var smartaData = new SmartaData
        {
            ReallocatedSectorCount = 50,
            PendingSectorCount = 20,
            PowerOnHours = 25000,
            Temperature = 55,
            UncorrectableErrorCount = 5
        };

        // Act
        var result = _calculator.CalculateQuality(smartaData);

        // Assert
        Assert.Equal(QualityGrade.D, result.Grade);
        Assert.True(result.Score >= 60);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void CalculateQualityFGradeWhenDiskFailed()
    {
        // Arrange
        var smartaData = new SmartaData
        {
            ReallocatedSectorCount = 500,
            PendingSectorCount = 100,
            PowerOnHours = 50000,
            Temperature = 70,
            UncorrectableErrorCount = 50
        };

        // Act
        var result = _calculator.CalculateQuality(smartaData);

        // Assert
        Assert.Equal(QualityGrade.F, result.Grade);
        Assert.True(result.Score < 50);
        Assert.True(result.Warnings.Count > 2);
    }

    [Fact]
    public void CalculateQualityPowerOnHoursPenaltyIncreases()
    {
        // Arrange
        var smartaData1 = new SmartaData { PowerOnHours = 2000, ReallocatedSectorCount = 0, PendingSectorCount = 0, Temperature = 35, UncorrectableErrorCount = 0 };
        var smartaData2 = new SmartaData { PowerOnHours = 40000, ReallocatedSectorCount = 0, PendingSectorCount = 0, Temperature = 35, UncorrectableErrorCount = 0 };

        // Act
        var result1 = _calculator.CalculateQuality(smartaData1);
        var result2 = _calculator.CalculateQuality(smartaData2);

        // Assert
        Assert.True(result2.Score < result1.Score);
    }

    [Fact]
    public void CalculateQualityTemperaturePenaltyIncreases()
    {
        // Arrange
        var smartaData1 = new SmartaData { Temperature = 40, ReallocatedSectorCount = 0, PendingSectorCount = 0, PowerOnHours = 500, UncorrectableErrorCount = 0 };
        var smartaData2 = new SmartaData { Temperature = 70, ReallocatedSectorCount = 0, PendingSectorCount = 0, PowerOnHours = 500, UncorrectableErrorCount = 0 };

        // Act
        var result1 = _calculator.CalculateQuality(smartaData1);
        var result2 = _calculator.CalculateQuality(smartaData2);

        // Assert
        Assert.True(result2.Score < result1.Score);
    }
}
