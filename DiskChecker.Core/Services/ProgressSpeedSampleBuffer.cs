using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Bounded, progress-oriented throughput history for long-running disk tests.
/// Retains the first, minimum, maximum and last measurement in every progress
/// bucket, in their original order. A stalled or oscillating device therefore
/// cannot push the beginning of the disk off the chart or exhaust memory.
/// </summary>
public sealed class ProgressSpeedSampleBuffer
{
    private readonly int _bucketCount;
    private readonly double _rangeStart;
    private readonly double _rangeEnd;
    private double _lastProgress = double.NegativeInfinity;
    private readonly List<SpeedSample> _samples = new();
    private int _lastBucketIndex = -1;
    private int _activeStartIndex;
    private long _sequence;
    private OrderedSample _first;
    private OrderedSample _minimum;
    private OrderedSample _maximum;
    private OrderedSample _last;

    private readonly record struct OrderedSample(SpeedSample Sample, long Order);

    /// <param name="bucketCount">Maximum number of progress buckets; each keeps at most four samples.</param>
    public ProgressSpeedSampleBuffer(int bucketCount = 2000, double rangeStart = 0, double rangeEnd = 100)
    {
        if (bucketCount < 1)
            throw new ArgumentOutOfRangeException(nameof(bucketCount));
        if (!double.IsFinite(rangeStart) || !double.IsFinite(rangeEnd) || rangeEnd <= rangeStart)
            throw new ArgumentOutOfRangeException(nameof(rangeEnd), "The progress window must be increasing.");
        _bucketCount = bucketCount;
        _rangeStart = rangeStart;
        _rangeEnd = rangeEnd;
    }

    public int MaxSamples => checked(_bucketCount * 4);
    public int Count => _samples.Count;

    /// <summary>Index where the current bucket begins; only this tail can change on Add.</summary>
    public int ActiveStartIndex => _activeStartIndex;

    public IReadOnlyList<SpeedSample> Samples => _samples;

    public void Add(SpeedSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var percent = double.IsFinite(sample.ProgressPercent)
            ? Math.Clamp(sample.ProgressPercent, _rangeStart, _rangeEnd) : _rangeStart;
        // Driver callbacks may arrive with a stale progress value. Never draw a
        // line backwards along the disk; the on-disk raw log still keeps the
        // original unmodified measurement for forensic analysis.
        if (percent < _lastProgress)
            percent = _lastProgress;
        _lastProgress = percent;
        if (sample.ProgressPercent != percent)
        {
            sample = new SpeedSample
            {
                Timestamp = sample.Timestamp,
                Elapsed = sample.Elapsed,
                ProgressPercent = percent,
                BytesProcessed = sample.BytesProcessed,
                SpeedMBps = sample.SpeedMBps,
                IsStalled = sample.IsStalled,
                Phase = sample.Phase
            };
        }
        var normalized = (percent - _rangeStart) / (_rangeEnd - _rangeStart);
        var bucketIndex = Math.Min(_bucketCount - 1, (int)(normalized * _bucketCount));

        // Progress is normally monotonic. Do not open new buckets if a driver
        // sends a slightly older progress value during a stalled I/O operation.
        bucketIndex = Math.Max(bucketIndex, _lastBucketIndex);
        var ordered = new OrderedSample(sample, ++_sequence);
        if (bucketIndex != _lastBucketIndex)
        {
            _lastBucketIndex = bucketIndex;
            _activeStartIndex = _samples.Count;
            _first = _minimum = _maximum = _last = ordered;
        }
        else
        {
            if (sample.SpeedMBps < _minimum.Sample.SpeedMBps)
                _minimum = ordered;
            if (sample.SpeedMBps > _maximum.Sample.SpeedMBps)
                _maximum = ordered;
            _last = ordered;
        }

        // Rebuild no more than four points at the tail. Earlier buckets are
        // finalized and are never removed or reprocessed.
        var orderedExtrema = new[] { _first, _minimum, _maximum, _last };
        Array.Sort(orderedExtrema, static (a, b) => a.Order.CompareTo(b.Order));
        _samples.RemoveRange(_activeStartIndex, _samples.Count - _activeStartIndex);
        long previousOrder = -1;
        foreach (var entry in orderedExtrema)
        {
            if (entry.Order == previousOrder)
                continue;
            _samples.Add(entry.Sample);
            previousOrder = entry.Order;
        }
    }

    public void Clear()
    {
        _samples.Clear();
        _lastBucketIndex = -1;
        _lastProgress = double.NegativeInfinity;
        _activeStartIndex = 0;
        _sequence = 0;
        _first = _minimum = _maximum = _last = default;
    }
}
