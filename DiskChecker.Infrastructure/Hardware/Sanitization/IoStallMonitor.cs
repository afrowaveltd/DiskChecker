using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware.Sanitization;

/// <summary>
/// Emits progress while a single disk I/O operation is still outstanding so device stalls are visible.
/// This helper does not start another I/O until the current one completes.
/// </summary>
internal static class IoStallMonitor
{
    internal static readonly TimeSpan DefaultStallReportThreshold = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan DefaultReportInterval = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan DefaultWarningThreshold = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(2);

    internal static async Task<MonitoredIoResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<TimeSpan, TimeSpan, SanitizationProgress> stalledProgressFactory,
        IProgress<SanitizationProgress>? progress,
        ILogger? logger,
        string phase,
        long offsetBytes,
        CancellationToken cancellationToken,
        TimeSpan? stallReportThreshold = null,
        TimeSpan? reportInterval = null,
        TimeSpan? warningThreshold = null,
        TimeSpan? operationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(stalledProgressFactory);

        var threshold = stallReportThreshold ?? DefaultStallReportThreshold;
        var interval = reportInterval ?? DefaultReportInterval;
        var warningAt = warningThreshold ?? DefaultWarningThreshold;
        var timeout = operationTimeout ?? DefaultOperationTimeout;
        var stopwatch = Stopwatch.StartNew();
        var task = operation(cancellationToken);
        var stalledReported = false;
        var warningReported = false;
        TimeSpan totalReportedStall = TimeSpan.Zero;

        while (!task.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var delay = Task.Delay(interval, cancellationToken);
            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == task)
            {
                break;
            }

            var elapsed = stopwatch.Elapsed;
            if (elapsed >= threshold)
            {
                var stallDuration = elapsed - threshold;
                totalReportedStall = stallDuration;
                stalledReported = true;

                var sample = stalledProgressFactory(elapsed, stallDuration);
                sample.IsStalled = true;
                sample.IsWaitingForDevice = true;
                sample.CurrentOperationElapsed = elapsed;
                sample.StallDuration = stallDuration;
                sample.CurrentSpeedMBps = 0;
                sample.RawOperationSpeedMBps = 0;
                sample.StatusDetail ??= $"Waiting for device response... stalled for {stallDuration:g}.";
                progress?.Report(sample);
            }

            if (!warningReported && elapsed >= warningAt)
            {
                warningReported = true;
                logger?.LogWarning(
                    "Disk I/O stall during {Phase} at offset {Offset}. Operation has been outstanding for {Elapsed}.",
                    phase,
                    offsetBytes,
                    elapsed);
            }

            if (timeout > TimeSpan.Zero && elapsed >= timeout)
            {
                // TODO: Replace this fail-fast timeout with a platform-specific recovery policy.
                throw new TimeoutException(
                    $"Device did not respond during {phase} at offset {offsetBytes:#,0} for {elapsed:g}.");
            }
        }

        var value = await task.ConfigureAwait(false);
        stopwatch.Stop();
        return new MonitoredIoResult<T>(value, stopwatch.Elapsed, stalledReported, totalReportedStall);
    }
}

internal sealed record MonitoredIoResult<T>(
    T Value,
    TimeSpan OperationElapsed,
    bool WasStalled,
    TimeSpan StallDuration);
