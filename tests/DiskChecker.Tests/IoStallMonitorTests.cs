using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware.Sanitization;
using Xunit;

namespace DiskChecker.Tests;

public class IoStallMonitorTests
{
    [Fact]
    public async Task FastOperation_CompletesBeforeThreshold_ReportsNoStall()
    {
        var samples = new List<SanitizationProgress>();

        var result = await IoStallMonitor.ExecuteAsync(
            _ => Task.FromResult(42),
            (elapsed, stall) => CreateProgress(100, elapsed, stall),
            new CallbackProgress<SanitizationProgress>(samples.Add),
            logger: null,
            phase: "Write",
            offsetBytes: 0,
            TestContext.Current.CancellationToken,
            stallReportThreshold: TimeSpan.FromSeconds(1),
            reportInterval: TimeSpan.FromMilliseconds(20));

        Assert.Equal(42, result.Value);
        Assert.False(result.WasStalled);
        Assert.Empty(samples);
    }

    [Fact]
    public async Task SlowOperation_ReportsStallsAndThenCompletes()
    {
        var samples = new List<SanitizationProgress>();

        var result = await IoStallMonitor.ExecuteAsync(
            async ct =>
            {
                await Task.Delay(160, ct);
                return 64;
            },
            (elapsed, stall) => CreateProgress(1234, elapsed, stall),
            new CallbackProgress<SanitizationProgress>(samples.Add),
            logger: null,
            phase: "Read",
            offsetBytes: 1234,
            TestContext.Current.CancellationToken,
            stallReportThreshold: TimeSpan.FromMilliseconds(50),
            reportInterval: TimeSpan.FromMilliseconds(25));

        Assert.Equal(64, result.Value);
        Assert.True(result.WasStalled);
        Assert.NotEmpty(samples);
        Assert.All(samples, sample =>
        {
            Assert.True(sample.IsStalled);
            Assert.True(sample.IsWaitingForDevice);
            Assert.Equal(1234, sample.BytesProcessed);
            Assert.Equal(0, sample.CurrentSpeedMBps);
        });
        Assert.True(result.OperationElapsed >= TimeSpan.FromMilliseconds(140));
    }

    [Fact]
    public async Task SlowOperationThatThrows_ReportsStallBeforeFailure()
    {
        var samples = new List<SanitizationProgress>();

        var ex = await Assert.ThrowsAsync<IOException>(() => IoStallMonitor.ExecuteAsync<int>(
            async ct =>
            {
                await Task.Delay(120, ct);
                throw new IOException("No such device");
            },
            (elapsed, stall) => CreateProgress(4096, elapsed, stall),
            new CallbackProgress<SanitizationProgress>(samples.Add),
            logger: null,
            phase: "Read",
            offsetBytes: 4096,
            TestContext.Current.CancellationToken,
            stallReportThreshold: TimeSpan.FromMilliseconds(40),
            reportInterval: TimeSpan.FromMilliseconds(25)));

        Assert.Contains("No such device", ex.Message);
        Assert.NotEmpty(samples);
        Assert.All(samples, sample => Assert.Equal(4096, sample.BytesProcessed));
    }

    [Fact]
    public async Task WaitingOperation_CanBeCanceledCleanly()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => IoStallMonitor.ExecuteAsync<int>(
            async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return 1;
            },
            (elapsed, stall) => CreateProgress(0, elapsed, stall),
            new CallbackProgress<SanitizationProgress>(_ => { }),
            logger: null,
            phase: "Write",
            offsetBytes: 0,
            cts.Token,
            stallReportThreshold: TimeSpan.FromMilliseconds(20),
            reportInterval: TimeSpan.FromMilliseconds(20)));
    }

    private static SanitizationProgress CreateProgress(long bytesProcessed, TimeSpan elapsed, TimeSpan stall)
    {
        return new SanitizationProgress
        {
            PhaseKind = SanitizationProgressPhase.Write,
            Phase = "Zápis nul",
            ProgressPercent = 10,
            BytesProcessed = bytesProcessed,
            TotalBytes = 10_000,
            CurrentOperationElapsed = elapsed,
            StallDuration = stall,
            EffectiveSpeedMBps = 0
        };
    }

    private sealed class CallbackProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public CallbackProgress(Action<T> callback)
        {
            _callback = callback;
        }

        public void Report(T value) => _callback(value);
    }
}
