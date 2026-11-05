using System;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware.Sanitization;
using Xunit;

namespace DiskChecker.Tests;

public sealed class SanitizationWritePacingTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(60, true)]
    public void SecondsSettingHasSafeZeroDefault(int seconds, bool expectedEnabled)
    {
        var settings = SanitizationWritePacing.FromSeconds(seconds);
        Assert.Equal(expectedEnabled, settings is not null);
        if (settings is not null)
        {
            Assert.Equal(SanitizationWritePacing.DefaultIntervalBytes, settings.IntervalBytes);
            Assert.Equal(TimeSpan.FromSeconds(seconds), settings.PauseDuration);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public void InvalidSecondsAreRejected(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SanitizationWritePacing.FromSeconds(seconds));

    [Fact]
    public async Task PauseOccursAtExactBoundaryNotAfterLastWrite()
    {
        var settings = new SanitizationWritePacing(4096, TimeSpan.FromMilliseconds(10));
        var controller = new SanitizationWritePacingController(settings);
        const long totalBytes = 3 * 4096 + 1024;
        long completed = 0;
        while (completed < totalBytes)
        {
            int count = controller.NextWriteLength(8 * 1024, completed, totalBytes);
            Assert.InRange(count, 1, 8192);
            completed += count;
            await controller.PauseIfDueAsync(completed, totalBytes, null,
                TestContext.Current.CancellationToken);
        }
        Assert.Equal(totalBytes, completed);
        Assert.Equal(3, controller.PauseCount);
        Assert.True(controller.TotalPaused >= TimeSpan.FromMilliseconds(20));
        Assert.True(controller.ActiveElapsed(controller.TotalPaused + TimeSpan.FromSeconds(2))
            >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task NoPauseAtEndOfExactBoundary()
    {
        var controller = new SanitizationWritePacingController(
            new SanitizationWritePacing(4096, TimeSpan.FromMilliseconds(1)));
        Assert.Equal(4096, controller.NextWriteLength(8192, 0, 4096));
        await controller.PauseIfDueAsync(4096, 4096, null,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, controller.PauseCount);
        Assert.Equal(TimeSpan.Zero, controller.TotalPaused);
    }

    [Fact]
    public async Task CancellationStopsPauseWithoutAdvancingWrite()
    {
        var controller = new SanitizationWritePacingController(
            new SanitizationWritePacing(4096, TimeSpan.FromSeconds(1)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.PauseIfDueAsync(4096, 8192, null, cts.Token));
    }

    [Fact]
    public void RejectsUnalignedThreshold()
    {
        Assert.Throws<ArgumentException>(() => new SanitizationWritePacingController(
            new SanitizationWritePacing(5000, TimeSpan.FromSeconds(1))));
    }
}
