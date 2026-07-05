using System;
using System.Collections.Generic;
using System.Linq;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Dual-level adaptive speed sampler.
/// 
/// Maintains two sample streams:
/// 1. Standard samples — evenly distributed ~200 points for chart display and DB storage.
/// 2. High-resolution anomaly samples — captured at 100ms intervals when speed deviation
///    exceeds a threshold, with hysteresis to avoid flickering.
/// 
/// This enables:
/// - Normal chart rendering with manageable data volume.
/// - Detailed reconstruction of performance drops/spikes.
/// - Overlay comparison of write vs read anomalies at the same disk position.
/// </summary>
public class AdaptiveSpeedSampler
{
    // ── Configuration ──────────────────────────────────────────
    
    /// <summary>Target number of standard samples for the full test.</summary>
    public int TargetStandardSamples { get; set; } = 200;

    /// <summary>Minimum interval between standard samples in milliseconds.</summary>
    public int MinStandardIntervalMs { get; set; } = 100;

    /// <summary>High-resolution sample interval during anomaly (ms).</summary>
    public int HighResIntervalMs { get; set; } = 100;

    /// <summary>
    /// Speed deviation threshold (%) to trigger anomaly recording.
    /// When current speed differs from the rolling baseline by more than this,
    /// high-resolution sampling begins.
    /// </summary>
    public double AnomalyThresholdPercent { get; set; } = 15.0;

    /// <summary>
    /// Hysteresis: anomaly recording stops when deviation drops below
    /// (threshold - hysteresis) percent.
    /// </summary>
    public double HysteresisPercent { get; set; } = 5.0;

    /// <summary>Minimum anomaly duration in ms to be recorded (filters noise).</summary>
    public int MinAnomalyDurationMs { get; set; } = 300;

    /// <summary>Window size for rolling baseline calculation.</summary>
    public int BaselineWindowSize { get; set; } = 10;

    // ── State ──────────────────────────────────────────────────

    private readonly List<SpeedSample> _standardSamples = new();
    private readonly List<SpeedAnomaly> _anomalies = new();
    private readonly List<SpeedSample> _baselineWindow = new();
    private readonly List<SpeedSample> _currentAnomalySamples = new();
    
    private DateTime _lastStandardSampleTime = DateTime.MinValue;
    private DateTime _testStartTime;
    private long _totalBytes;
    private bool _inAnomaly;
    private double _entrySpeed;
    private double _frozenBaseline; // Baseline frozen at anomaly start, avoids contamination
    private int _anomalyStartStandardIndex;
    private double _anomalyStartProgress;
    private string _phase = "Write";
    private int _anomalyGroupCounter;

    // ── Public API ─────────────────────────────────────────────

    /// <summary>All standard (downsampled) samples collected.</summary>
    public IReadOnlyList<SpeedSample> StandardSamples => _standardSamples;

    /// <summary>All detected anomalies with high-resolution data.</summary>
    public IReadOnlyList<SpeedAnomaly> Anomalies => _anomalies;

    /// <summary>Current phase name (Write/Read).</summary>
    public string Phase
    {
        get => _phase;
        set
        {
            // When phase changes, finalize any open anomaly
            if (_inAnomaly)
                FinalizeAnomaly();
            _phase = value;
            _baselineWindow.Clear();
            _anomalyGroupCounter = 0;
        }
    }

    /// <summary>
    /// Initialize the sampler for a new test phase.
    /// Does NOT clear anomalies — they persist across phases for final analysis.
    /// </summary>
    /// <param name="totalBytes">Total bytes to be processed in this phase.</param>
    public void Initialize(long totalBytes)
    {
        _standardSamples.Clear();
        _baselineWindow.Clear();
        _currentAnomalySamples.Clear();
        _lastStandardSampleTime = DateTime.MinValue;
        _testStartTime = DateTime.UtcNow;
        _totalBytes = totalBytes;
        _inAnomaly = false;
        _anomalyGroupCounter = 0;
        // NOTE: _anomalies is NOT cleared — they persist across phases
    }

    /// <summary>
    /// Feed a raw speed measurement. The sampler decides whether to:
    /// - Add to standard samples (if enough time elapsed)
    /// - Start/continue/stop high-resolution anomaly recording
    /// </summary>
    /// <param name="speedMBps">Current speed in MB/s.</param>
    /// <param name="bytesProcessed">Cumulative bytes processed so far.</param>
    /// <param name="timestamp">UTC timestamp of the measurement.</param>
    public void AddSample(double speedMBps, long bytesProcessed, DateTime timestamp)
    {
        var progress = _totalBytes > 0 ? (bytesProcessed / (double)_totalBytes) * 100.0 : 0;
        var sample = new SpeedSample
        {
            Timestamp = timestamp,
            SpeedMBps = speedMBps,
            ProgressPercent = progress,
            BytesProcessed = bytesProcessed
        };

        // ── Standard sampling (time-based decimation) ──
        var elapsedSinceLastStandard = (timestamp - _lastStandardSampleTime).TotalMilliseconds;
        if (elapsedSinceLastStandard >= MinStandardIntervalMs)
        {
            _standardSamples.Add(sample);
            _lastStandardSampleTime = timestamp;
        }

        // ── Baseline tracking ──
        _baselineWindow.Add(sample);
        while (_baselineWindow.Count > BaselineWindowSize)
            _baselineWindow.RemoveAt(0);

        // ── Anomaly detection ──
        if (_baselineWindow.Count >= 3)
        {
            // Use frozen baseline during anomaly, rolling baseline otherwise
            var baseline = _inAnomaly
                ? _frozenBaseline
                : _baselineWindow.Take(_baselineWindow.Count - 1).Average(s => s.SpeedMBps);

            if (baseline > 0)
            {
                var deviation = Math.Abs(speedMBps - baseline) / baseline * 100.0;

                if (!_inAnomaly)
                {
                    // Start anomaly if deviation exceeds threshold
                    if (deviation >= AnomalyThresholdPercent)
                    {
                        _inAnomaly = true;
                        _entrySpeed = baseline;
                        _frozenBaseline = baseline; // Freeze baseline to avoid contamination
                        _anomalyStartStandardIndex = _standardSamples.Count > 0 ? _standardSamples.Count - 1 : 0;
                        _anomalyStartProgress = _standardSamples.Count > 0 ? _standardSamples[^1].ProgressPercent : progress;
                        _currentAnomalySamples.Clear();
                        // Include the last few baseline samples for context
                        foreach (var bs in _baselineWindow.Take(_baselineWindow.Count - 1))
                            _currentAnomalySamples.Add(bs);
                    }
                }
                else
                {
                    // Continue anomaly: record high-res sample
                    _currentAnomalySamples.Add(sample);

                    // End anomaly if deviation drops below (threshold - hysteresis)
                    // compared to the FROZEN baseline (not the contaminated rolling one)
                    if (deviation < (AnomalyThresholdPercent - HysteresisPercent))
                    {
                        FinalizeAnomaly();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Call at the end of a phase to finalize any open anomaly and
    /// optionally downsample standard samples to the target count.
    /// </summary>
    public void FinalizePhase()
    {
        if (_inAnomaly)
            FinalizeAnomaly();

        // Downsample standard samples to target count if we have too many
        if (_standardSamples.Count > TargetStandardSamples)
        {
            var downsampled = DownsampleToTarget(_standardSamples, TargetStandardSamples);
            _standardSamples.Clear();
            _standardSamples.AddRange(downsampled);
        }
    }

    /// <summary>
    /// Get the standard samples as a list (for DB persistence).
    /// </summary>
    public List<SpeedSample> GetStandardSamples() => _standardSamples.ToList();

    /// <summary>
    /// Get all detected anomalies (for DB persistence).
    /// </summary>
    public List<SpeedAnomaly> GetAnomalies() => _anomalies.ToList();

    // ── Private helpers ────────────────────────────────────────

    private void FinalizeAnomaly()
    {
        if (_currentAnomalySamples.Count < 2)
        {
            _currentAnomalySamples.Clear();
            _baselineWindow.Clear();
            _inAnomaly = false;
            return;
        }

        // Find the first sample that actually deviates from the frozen baseline
        // (skip context/baseline samples that were prepended)
        int firstDeviatingIndex = 0;
        for (int i = 0; i < _currentAnomalySamples.Count; i++)
        {
            var dev = _frozenBaseline > 0
                ? Math.Abs(_currentAnomalySamples[i].SpeedMBps - _frozenBaseline) / _frozenBaseline * 100.0
                : 0;
            if (dev >= AnomalyThresholdPercent)
            {
                firstDeviatingIndex = i;
                break;
            }
        }

        var actualAnomalySamples = _currentAnomalySamples.Skip(firstDeviatingIndex).ToList();
        if (actualAnomalySamples.Count < 2)
        {
            _currentAnomalySamples.Clear();
            _baselineWindow.Clear();
            _inAnomaly = false;
            return;
        }

        var durationMs = (actualAnomalySamples[^1].Timestamp - actualAnomalySamples[0].Timestamp).TotalMilliseconds;

        // Filter out noise: anomalies shorter than MinAnomalyDurationMs
        if (durationMs < MinAnomalyDurationMs)
        {
            _currentAnomalySamples.Clear();
            _baselineWindow.Clear(); // Must clear to avoid contaminated baseline triggering false anomalies
            _inAnomaly = false;
            return;
        }

        var speeds = actualAnomalySamples.Select(s => s.SpeedMBps).ToList();
        var minSpeed = speeds.Min();
        var maxSpeed = speeds.Max();
        var avgSpeed = speeds.Average();
        var exitSpeed = speeds.Count >= 3 ? speeds.TakeLast(3).Average() : speeds[^1];
        var maxDeviation = _frozenBaseline > 0
            ? Math.Max(Math.Abs(minSpeed - _frozenBaseline), Math.Abs(maxSpeed - _frozenBaseline)) / _frozenBaseline * 100.0
            : 0;

        var endStandardIndex = _standardSamples.Count > 0 ? _standardSamples.Count - 1 : _anomalyStartStandardIndex;
        var endProgress = _standardSamples.Count > 0 ? _standardSamples[^1].ProgressPercent : _anomalyStartProgress;

        _anomalyGroupCounter++;
        var overlayGroup = $"{_phase}_{_anomalyGroupCounter}_{_anomalyStartProgress:F0}";

        var anomaly = new SpeedAnomaly
        {
            Phase = _phase,
            StartStandardIndex = _anomalyStartStandardIndex,
            EndStandardIndex = endStandardIndex,
            StartProgressPercent = _anomalyStartProgress,
            EndProgressPercent = endProgress,
            StartBytesProcessed = actualAnomalySamples[0].BytesProcessed,
            EndBytesProcessed = actualAnomalySamples[^1].BytesProcessed,
            StartLba512 = actualAnomalySamples[0].BytesProcessed / 512L,
            EndLba512 = actualAnomalySamples[^1].BytesProcessed / 512L,
            DurationMs = durationMs,
            MinSpeedMBps = minSpeed,
            MaxSpeedMBps = maxSpeed,
            AvgSpeedMBps = avgSpeed,
            EntrySpeedMBps = _frozenBaseline,
            ExitSpeedMBps = exitSpeed,
            MaxDeviationPercent = Math.Round(maxDeviation, 1),
            HighResSamples = actualAnomalySamples.ToList(),
            OverlayGroup = overlayGroup
        };
        anomaly.ComputeSeverity();

        _anomalies.Add(anomaly);
        _currentAnomalySamples.Clear();
        _baselineWindow.Clear(); // Reset baseline to avoid contamination from anomaly samples
        _inAnomaly = false;
    }

    private static List<SpeedSample> DownsampleToTarget(List<SpeedSample> samples, int targetCount)
    {
        if (samples.Count <= targetCount)
            return samples.ToList();

        var result = new List<SpeedSample>(targetCount);
        var bucketSize = samples.Count / (double)targetCount;

        for (var i = 0; i < targetCount; i++)
        {
            var start = (int)Math.Floor(i * bucketSize);
            var end = (int)Math.Floor((i + 1) * bucketSize);
            end = Math.Clamp(end, start + 1, samples.Count);

            var sumSpeed = 0d;
            var sumBytes = 0L;
            for (var j = start; j < end; j++)
            {
                sumSpeed += samples[j].SpeedMBps;
                sumBytes += samples[j].BytesProcessed;
            }
            var count = end - start;
            result.Add(new SpeedSample
            {
                Timestamp = samples[start].Timestamp,
                SpeedMBps = sumSpeed / count,
                ProgressPercent = samples[end - 1].ProgressPercent,
                BytesProcessed = sumBytes / count
            });
        }

        return result;
    }
}
