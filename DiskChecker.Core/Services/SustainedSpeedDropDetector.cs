using System;

namespace DiskChecker.Core.Services;

/// <summary>Constant-memory local speed detector for consecutive measured active I/O intervals.</summary>
public sealed class SustainedSpeedDropDetector
{
    public const double DefaultDropRatio = 0.30;
    public static readonly TimeSpan DefaultMinimumDuration = TimeSpan.FromMilliseconds(500);
    private readonly double _dropRatio;
    private readonly double _minimumSeconds;
    private double _baseline, _pendingSeconds, _windowSeconds, _windowTransfer;

    public int Count { get; private set; }
    public bool HasMeasurements { get; private set; }
    public double WorstConfirmedDropRatio { get; private set; }

    public SustainedSpeedDropDetector(double dropRatio = DefaultDropRatio, TimeSpan? minimumDuration = null)
    {
        if (!double.IsFinite(dropRatio) || dropRatio <= 0 || dropRatio >= 1)
            throw new ArgumentOutOfRangeException(nameof(dropRatio));
        var duration = minimumDuration ?? DefaultMinimumDuration;
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumDuration));
        _dropRatio = dropRatio;
        _minimumSeconds = duration.TotalSeconds;
    }

    /// <summary>Discard unfinished evidence at a phase boundary, retaining confirmed counts.</summary>
    public void ResetBaseline()
    {
        _baseline = _pendingSeconds = _windowSeconds = _windowTransfer = 0;
    }

    /// <summary>Time-weight tiny operations into approximately 100 ms windows.
    /// Supply actual I/O time, not planned pauses or gaps between display points.</summary>
    public void Observe(double speed, TimeSpan activeDuration)
    {
        var seconds = activeDuration.TotalSeconds;
        if (!double.IsFinite(speed) || speed < 0 || seconds <= 0)
        {
            ResetBaseline();
            return;
        }
        HasMeasurements = true;
        _windowSeconds += seconds;
        _windowTransfer += speed * seconds;
        if (_windowSeconds < 0.1 - 1e-12) return;
        var measured = _windowTransfer / _windowSeconds;
        var duration = _windowSeconds;
        _windowSeconds = _windowTransfer = 0;
        if (_baseline <= 0)
        {
            _baseline = measured;
            return;
        }

        var drop = 1 - measured / _baseline;
        if (drop <= _dropRatio + 1e-12)
        {
            _pendingSeconds = 0;
            _baseline = measured;
            return;
        }

        // Freeze the pre-drop baseline until recovery or confirmation.
        _pendingSeconds += duration;
        if (_pendingSeconds <= _minimumSeconds + 1e-12) return;
        Count++;
        WorstConfirmedDropRatio = Math.Max(WorstConfirmedDropRatio, drop);
        _pendingSeconds = 0;
        _baseline = measured; // a persistent new level is counted once
    }
}
