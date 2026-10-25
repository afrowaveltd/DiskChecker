using System;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

public class DiskTemperatureMonitorTests
{
    private static ISmartaProvider CreateProvider(Func<string, CancellationToken, Task<int?>>? temperature = null)
    {
        var provider = Substitute.For<ISmartaProvider>();
        if (temperature != null)
        {
            provider.GetTemperatureOnlyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => temperature(ci.ArgAt<string>(0), ci.ArgAt<CancellationToken>(1)));
        }
        return provider;
    }

    [Fact]
    public async Task ReadOnceAsync_ReportsTemperature_FromProvider()
    {
        var provider = CreateProvider((_, _) => Task.FromResult<int?>(42));
        using var monitor = new DiskTemperatureMonitor(provider);
        monitor.Start("/dev/sda", supportsSmart: true);

        var result = await monitor.ReadOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
        Assert.Equal(42, monitor.CurrentTemperature);
        Assert.Equal(42, monitor.MinTemperature);
        Assert.Equal(42, monitor.MaxTemperature);
    }

    [Fact]
    public async Task ReadOnceAsync_SwallowsProviderException_AndKeepsLastValue()
    {
        var provider = CreateProvider((_, _) => Task.FromResult<int?>(40));
        using var monitor = new DiskTemperatureMonitor(provider);
        monitor.Start("/dev/sda", supportsSmart: true);

        await monitor.ReadOnceAsync(TestContext.Current.CancellationToken);

        // Now make the provider throw.
        var throwing = CreateProvider((_, _) => throw new InvalidOperationException("boom"));
        using var monitor2 = new DiskTemperatureMonitor(throwing);
        monitor2.Start("/dev/sda", supportsSmart: true);
        var result = await monitor2.ReadOnceAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Null(monitor2.CurrentTemperature);
    }

    [Fact]
    public async Task ReadOnceAsync_ReturnsNull_WhenSmartUnsupported()
    {
        var provider = CreateProvider();
        using var monitor = new DiskTemperatureMonitor(provider);
        monitor.Start("/dev/sda", supportsSmart: false);

        var result = await monitor.ReadOnceAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task ThresholdExceeded_Raises_WhenTemperatureCrossesThreshold()
    {
        var provider = CreateProvider((_, _) => Task.FromResult<int?>(65));
        using var monitor = new DiskTemperatureMonitor(provider);
        monitor.Start("/dev/sda", supportsSmart: true, deviceType: "SATA/ATA", smartThreshold: 60);

        int? raised = null;
        monitor.ThresholdExceeded += (_, t) => raised = t;

        await monitor.ReadOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(60, raised);
        Assert.True(monitor.IsOverThreshold);
    }

    [Fact]
    public async Task ThresholdExceeded_DoesNotRaise_WhenBelowThreshold()
    {
        var provider = CreateProvider((_, _) => Task.FromResult<int?>(50));
        using var monitor = new DiskTemperatureMonitor(provider);
        monitor.Start("/dev/sda", supportsSmart: true, deviceType: "SATA/ATA", smartThreshold: 60);

        int? raised = null;
        monitor.ThresholdExceeded += (_, t) => raised = t;

        await monitor.ReadOnceAsync(TestContext.Current.CancellationToken);

        Assert.Null(raised);
        Assert.False(monitor.IsOverThreshold);
    }

    [Fact]
    public void Threshold_FallsBackToDeviceType_WhenSmartThresholdAbsent()
    {
        var provider = CreateProvider();
        using var hdd = new DiskTemperatureMonitor(provider);
        hdd.Start("/dev/sda", supportsSmart: true, deviceType: "SATA/ATA", smartThreshold: null);
        Assert.Equal(60, hdd.SafeTemperatureThreshold);

        using var ssd = new DiskTemperatureMonitor(provider);
        ssd.Start("/dev/nvme0", supportsSmart: true, deviceType: "NVMe", smartThreshold: null);
        Assert.Equal(70, ssd.SafeTemperatureThreshold);
    }

    [Fact]
    public async Task GetSamples_ReturnsCollectedSamples_InOrder()
    {
        var provider = CreateProvider((_, _) => Task.FromResult<int?>(40));
        using var monitor = new DiskTemperatureMonitor(provider);
        monitor.Start("/dev/sda", supportsSmart: true);

        await monitor.ReadOnceAsync(TestContext.Current.CancellationToken);
        await monitor.ReadOnceAsync(TestContext.Current.CancellationToken);

        var samples = monitor.GetSamples();
        Assert.Equal(2, samples.Count);
        Assert.Equal(40, samples[0].TemperatureCelsius);
        Assert.Equal(40, samples[1].TemperatureCelsius);
    }

    [Fact]
    public void StopMonitoring_IsIdempotent_AndDisposeIsSafe()
    {
        var provider = CreateProvider();
        var monitor = new DiskTemperatureMonitor(provider);
        monitor.Start("/dev/sda", supportsSmart: true);

        monitor.StopMonitoring();
        monitor.StopMonitoring();
        monitor.Dispose();
        monitor.Dispose();
    }
}
