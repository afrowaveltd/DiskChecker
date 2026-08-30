using System.Collections.Generic;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Tests for <see cref="TestAnalysisDataService.GetAnalysisDataAsync"/> fallback
/// behavior: legacy owned speed samples and session-level temperature metrics must
/// be surfaced so the Analysis card charts render for non-Seek/non-Sanitization tests.
/// </summary>
public class TestAnalysisDataServiceTests
{
    [Fact]
    public async Task GetAnalysisDataAsync_LoadsLegacySpeedSamples_WhenTelemetryEmpty()
    {
        var repository = Substitute.For<IDiskCardRepository>();
        var session = new TestSession { Id = 4, TestType = TestType.QuickRead };

        repository.GetTestSessionWithoutSamplesAsync(4).Returns(session);
        repository.GetTelemetrySamplesAsync(4).Returns(new List<TestTelemetrySample>());
        repository.GetAnomalyEventsAsync(4).Returns(new List<TestAnomalyEvent>());
        repository.GetStallEventsAsync(4).Returns(new List<TestStallEvent>());
        repository.GetSeekSamplesAsync(4).Returns(new List<SeekSampleRecord>());
        repository.GetTemperatureSampleSeriesAsync(4).Returns(new List<TemperatureSample>());
        repository.GetSpeedSampleSeriesAsync(4).Returns((
            new List<SpeedSample>
            {
                new() { ProgressPercent = 0, SpeedMBps = 100 },
                new() { ProgressPercent = 50, SpeedMBps = 150 },
                new() { ProgressPercent = 100, SpeedMBps = 120 },
            },
            new List<SpeedSample>
            {
                new() { ProgressPercent = 0, SpeedMBps = 200 },
                new() { ProgressPercent = 100, SpeedMBps = 180 },
            }));

        var service = new TestAnalysisDataService(repository);
        var data = await service.GetAnalysisDataAsync(4, TestContext.Current.CancellationToken);

        Assert.NotNull(data);
        Assert.Equal(5, data!.TelemetrySamples.Count);
        Assert.Contains(data.TelemetrySamples, s => s.Phase == TelemetrySamplePhase.Write);
        Assert.Contains(data.TelemetrySamples, s => s.Phase == TelemetrySamplePhase.Read);
    }

    [Fact]
    public async Task GetAnalysisDataAsync_SynthesizesTemperature_WhenNoSamples()
    {
        var repository = Substitute.For<IDiskCardRepository>();
        var session = new TestSession
        {
            Id = 6,
            TestType = TestType.Sanitization,
            StartTemperature = 30,
            MaxTemperature = 44,
            AverageTemperature = 37
        };

        repository.GetTestSessionWithoutSamplesAsync(6).Returns(session);
        repository.GetTelemetrySamplesAsync(6).Returns(new List<TestTelemetrySample>());
        repository.GetAnomalyEventsAsync(6).Returns(new List<TestAnomalyEvent>());
        repository.GetStallEventsAsync(6).Returns(new List<TestStallEvent>());
        repository.GetSeekSamplesAsync(6).Returns(new List<SeekSampleRecord>());
        repository.GetTemperatureSampleSeriesAsync(6).Returns(new List<TemperatureSample>());
        repository.GetSpeedSampleSeriesAsync(6).Returns((new List<SpeedSample>(), new List<SpeedSample>()));

        var service = new TestAnalysisDataService(repository);
        var data = await service.GetAnalysisDataAsync(6, TestContext.Current.CancellationToken);

        Assert.NotNull(data);
        Assert.Equal(2, data!.TemperatureSamples.Count);
        Assert.Equal(30, data.TemperatureSamples[0].TemperatureCelsius);
        Assert.Equal(44, data.TemperatureSamples[1].TemperatureCelsius);
    }

    [Fact]
    public async Task GetAnalysisDataAsync_KeepsTelemetry_WhenPresent()
    {
        var repository = Substitute.For<IDiskCardRepository>();
        var session = new TestSession { Id = 18, TestType = TestType.Sanitization };

        var telemetry = new List<TestTelemetrySample>
        {
            new() { Phase = TelemetrySamplePhase.Write, SpeedMBps = 100 },
            new() { Phase = TelemetrySamplePhase.Write, SpeedMBps = 120 },
        };

        repository.GetTestSessionWithoutSamplesAsync(18).Returns(session);
        repository.GetTelemetrySamplesAsync(18).Returns(telemetry);
        repository.GetAnomalyEventsAsync(18).Returns(new List<TestAnomalyEvent>());
        repository.GetStallEventsAsync(18).Returns(new List<TestStallEvent>());
        repository.GetSeekSamplesAsync(18).Returns(new List<SeekSampleRecord>());
        repository.GetTemperatureSampleSeriesAsync(18).Returns(new List<TemperatureSample>());
        repository.GetSpeedSampleSeriesAsync(18).Returns((new List<SpeedSample>(), new List<SpeedSample>()));

        var service = new TestAnalysisDataService(repository);
        var data = await service.GetAnalysisDataAsync(18, TestContext.Current.CancellationToken);

        Assert.NotNull(data);
        Assert.Equal(2, data!.TelemetrySamples.Count);
    }
}
