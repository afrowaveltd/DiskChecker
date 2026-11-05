using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;

namespace DiskChecker.Infrastructure.Hardware.Sanitization;

/// <summary>
/// Optional pacing for destructive zero-fill writes. Never applies to read/verify.
/// </summary>
public sealed class SanitizationWritePacingController
{
    private readonly SanitizationWritePacing? _settings;
    private long _nextBoundary;

    public SanitizationWritePacingController(SanitizationWritePacing? settings)
    {
        if (settings is { IntervalBytes: <= 0 })
            throw new ArgumentOutOfRangeException(nameof(settings), "Interval must be positive.");
        if (settings is { PauseDuration.Ticks: < 0 })
            throw new ArgumentOutOfRangeException(nameof(settings), "Pause duration must be nonnegative.");
        if (settings is { IntervalBytes: var interval } && interval % 4096 != 0)
            throw new ArgumentException("Pacing interval must be divisible by 4096 for direct I/O.", nameof(settings));

        _settings = settings;
        _nextBoundary = settings?.IntervalBytes ?? long.MaxValue;
    }

    public bool Enabled => _settings is not null;
    public int PauseCount { get; private set; }
    public TimeSpan TotalPaused { get; private set; }

    /// <summary>Clamp the next write so that a pause begins at the exact byte boundary.</summary>
    public int NextWriteLength(int preferredBytes, long completedBytes, long totalBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(preferredBytes);
        if (completedBytes < 0 || completedBytes >= totalBytes)
            throw new ArgumentOutOfRangeException(nameof(completedBytes));
        long untilPause = _nextBoundary > completedBytes ? _nextBoundary - completedBytes : long.MaxValue;
        return (int)Math.Min(Math.Min(preferredBytes, totalBytes - completedBytes), untilPause);
    }

    /// <summary>Pause only after full batches, never after the final write.</summary>
    public async Task PauseIfDueAsync(
        long completedBytes,
        long totalBytes,
        IProgress<SanitizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_settings is null || completedBytes < _nextBoundary || completedBytes >= totalBytes)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new SanitizationProgress
        {
            PhaseKind = SanitizationProgressPhase.Other,
            Phase = "SSD pacing pause",
            ProgressPercent = completedBytes * 100d / totalBytes,
            BytesProcessed = completedBytes,
            TotalBytes = totalBytes,
            CurrentSpeedMBps = 0,
            StatusDetail = $"Poor SSD Drive Sanity: {completedBytes / (1024d * 1024 * 1024):F1} GiB written; cooling down for {_settings.PauseDuration.TotalSeconds:F1} s."
        });

        var watch = Stopwatch.StartNew();
        try
        {
            await Task.Delay(_settings.PauseDuration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            watch.Stop();
            TotalPaused += watch.Elapsed;
            PauseCount++;
            _nextBoundary = _nextBoundary > long.MaxValue - _settings.IntervalBytes
                ? long.MaxValue : _nextBoundary + _settings.IntervalBytes;
        }
    }

    /// <summary>Elapsed time used for active-write throughput, excluding deliberate pauses.</summary>
    public TimeSpan ActiveElapsed(TimeSpan wallElapsed) =>
        wallElapsed > TotalPaused ? wallElapsed - TotalPaused : TimeSpan.Zero;
}
