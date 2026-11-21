using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Services;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public sealed class DiskComparisonServiceRegressionTests
{
    [Fact]
    public async Task UsesNewestSessionAndDoesNotDivideByZeroForReadOnlyTests()
    {
        var repo = Substitute.For<IDiskCardRepository>();
        var first = new DiskCard { Id = 1, ModelName = "First" };
        var second = new DiskCard { Id = 2, ModelName = "Second" };
        repo.GetByIdAsync(1).Returns(Task.FromResult<DiskCard?>(first));
        repo.GetByIdAsync(2).Returns(Task.FromResult<DiskCard?>(second));
        repo.GetTestSessionsAsync(1).Returns(Task.FromResult(new List<TestSession>
        {
            new() { StartedAt = new DateTime(2020, 1, 1), Score = 20 },
            new() { StartedAt = new DateTime(2026, 11, 1), Score = 90,
                AverageReadSpeedMBps = 100, BytesRead = 12345 }
        }));
        repo.GetTestSessionsAsync(2).Returns(Task.FromResult(new List<TestSession>
        {
            new() { StartedAt = new DateTime(2026, 11, 1), Score = 75,
                AverageReadSpeedMBps = 80, BytesRead = 12345 }
        }));
        var service = new DiskComparisonService(repo);

        var result = await service.CompareAsync(new List<int> { 1, 2 });
        Assert.Equal(90, result[0].Score);
        Assert.False(result[0].HasWriteMeasurement);
        Assert.Equal("N/A", result[0].WriteSpeedText);

        var performance = await service.ComparePerformanceAsync(1, 2);
        Assert.InRange(performance.SpeedAdvantagePercent, 0, 1000);
    }
}
