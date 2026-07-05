using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Reduces high-frequency throughput samples into a persistent research/analysis series.
/// The reducer keeps graph shape, time/progress anchors, stalls and significant speed changes
/// while avoiding unbounded database growth for long destructive tests.
/// </summary>
public static class SpeedSampleRetentionService
{
    public static IReadOnlyList<SpeedSample> ReduceForPersistence(
        IReadOnlyCollection<SpeedSample>? samples,
        TelemetryRetentionProfile profile = TelemetryRetentionProfile.Balanced)
    {
        return ReduceForPersistenceWithReasons(samples, profile)
            .Select(r => r.Sample)
            .ToList();
    }

    public static IReadOnlyList<RetainedSpeedSample> ReduceForPersistenceWithReasons(
        IReadOnlyCollection<SpeedSample>? samples,
        TelemetryRetentionProfile profile = TelemetryRetentionProfile.Balanced)
    {
        if (samples == null || samples.Count == 0)
        {
            return Array.Empty<RetainedSpeedSample>();
        }

        var options = TelemetryRetentionOptions.ForProfile(profile);
        var ordered = samples
            .Where(s => s.SpeedMBps > 0 || s.IsStalled)
            .OrderBy(s => s.Timestamp == default ? DateTime.MaxValue : s.Timestamp)
            .ThenBy(s => s.ProgressPercent)
            .ToList();

        if (ordered.Count == 0)
        {
            return Array.Empty<RetainedSpeedSample>();
        }

        if (ordered.Count <= options.TargetSamples)
        {
            return ordered
                .Select((sample, index) => new RetainedSpeedSample(sample, GetRawReason(sample, index, ordered.Count)))
                .ToList();
        }

        var reasons = new SortedSet<string>[ordered.Count];
        for (var i = 0; i < reasons.Length; i++)
        {
            reasons[i] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        Keep(reasons, 0, "First");
        Keep(reasons, ordered.Count - 1, "Last");

        var nonZeroSpeeds = ordered.Where(s => s.SpeedMBps > 0).Select(s => s.SpeedMBps).ToList();
        var averageSpeed = nonZeroSpeeds.Count > 0 ? nonZeroSpeeds.Average() : 0;
        var speedThreshold = Math.Max(options.MinAbsoluteSpeedChangeMBps, averageSpeed * options.RelativeSpeedChangeThreshold);

        // Preserve a uniform baseline so completely stable media still have a useful time/progress axis.
        var baselineTarget = Math.Max(2, options.TargetSamples / 2);
        var baselineStep = Math.Max(1, ordered.Count / baselineTarget);
        for (var i = 0; i < ordered.Count; i += baselineStep)
        {
            Keep(reasons, i, "Baseline");
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var sample = ordered[i];

            if (sample.IsStalled)
            {
                KeepWithContext(reasons, i, options.ContextSamplesAroundEvents, "Stall", "StallContext");
                continue;
            }

            if (i > 0 && Math.Abs(sample.SpeedMBps - ordered[i - 1].SpeedMBps) >= speedThreshold)
            {
                KeepWithContext(reasons, i, 1, "SpeedChange", "SpeedChangeContext");
            }

            if (i < ordered.Count - 1 && Math.Abs(ordered[i + 1].SpeedMBps - sample.SpeedMBps) >= speedThreshold)
            {
                KeepWithContext(reasons, i, 1, "SpeedChange", "SpeedChangeContext");
            }

            // Local extrema are important for later zoomed analysis of dips/spikes.
            if (i > 0 && i < ordered.Count - 1)
            {
                var prev = ordered[i - 1].SpeedMBps;
                var curr = sample.SpeedMBps;
                var next = ordered[i + 1].SpeedMBps;
                var isLocalMin = curr < prev && curr <= next && Math.Abs(prev - curr) >= speedThreshold;
                var isLocalMax = curr > prev && curr >= next && Math.Abs(curr - prev) >= speedThreshold;
                if (isLocalMin)
                {
                    KeepWithContext(reasons, i, 1, "LocalMin", "ExtremaContext");
                }
                else if (isLocalMax)
                {
                    KeepWithContext(reasons, i, 1, "LocalMax", "ExtremaContext");
                }
            }
        }

        var reduced = Materialize(ordered, reasons);
        if (reduced.Count <= options.MaxSamples)
        {
            return reduced;
        }

        return TrimToMaxSamples(reduced, options.MaxSamples, options.ContextSamplesAroundEvents);
    }

    private static string GetRawReason(SpeedSample sample, int index, int count)
    {
        if (sample.IsStalled) return "Stall";
        if (index == 0) return "First";
        if (index == count - 1) return "Last";
        return "Raw";
    }

    private static void Keep(SortedSet<string>[] reasons, int index, string reason)
    {
        if ((uint)index < (uint)reasons.Length)
        {
            reasons[index].Add(reason);
        }
    }

    private static void KeepWithContext(SortedSet<string>[] reasons, int index, int context, string reason, string contextReason)
    {
        for (var i = index - context; i <= index + context; i++)
        {
            Keep(reasons, i, i == index ? reason : contextReason);
        }
    }

    private static List<RetainedSpeedSample> Materialize(IReadOnlyList<SpeedSample> ordered, SortedSet<string>[] reasons)
    {
        var result = new List<RetainedSpeedSample>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (reasons[i].Count > 0)
            {
                result.Add(new RetainedSpeedSample(ordered[i], string.Join("+", reasons[i])));
            }
        }
        return result;
    }

    private static IReadOnlyList<RetainedSpeedSample> TrimToMaxSamples(IReadOnlyList<RetainedSpeedSample> samples, int maxSamples, int context)
    {
        if (samples.Count <= maxSamples)
        {
            return samples.ToList();
        }

        var reasons = new SortedSet<string>[samples.Count];
        for (var i = 0; i < reasons.Length; i++)
        {
            reasons[i] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        Keep(reasons, 0, AppendReason(samples[0].RetentionReason, "TrimFirst"));
        Keep(reasons, samples.Count - 1, AppendReason(samples[^1].RetentionReason, "TrimLast"));

        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i].Sample.IsStalled || samples[i].RetentionReason.Contains("Stall", StringComparison.OrdinalIgnoreCase))
            {
                for (var j = i - context; j <= i + context; j++)
                {
                    if ((uint)j < (uint)reasons.Length)
                    {
                        reasons[j].Add(AppendReason(samples[j].RetentionReason, j == i ? "TrimStall" : "TrimStallContext"));
                    }
                }
            }
        }

        var reservedCount = reasons.Count(v => v.Count > 0);
        var remainingBudget = Math.Max(0, maxSamples - reservedCount);
        var step = remainingBudget > 0 ? samples.Count / (double)remainingBudget : double.MaxValue;
        for (var n = 0; n < remainingBudget; n++)
        {
            var idx = (int)Math.Floor(n * step);
            if ((uint)idx < (uint)reasons.Length)
            {
                reasons[idx].Add(AppendReason(samples[idx].RetentionReason, "TrimBaseline"));
            }
        }

        var result = new List<RetainedSpeedSample>(maxSamples);
        for (var i = 0; i < samples.Count && result.Count < maxSamples; i++)
        {
            if (reasons[i].Count > 0)
            {
                result.Add(new RetainedSpeedSample(samples[i].Sample, string.Join("+", reasons[i])));
            }
        }
        return result;
    }

    private static string AppendReason(string existing, string reason)
    {
        return string.IsNullOrWhiteSpace(existing) ? reason : existing + "+" + reason;
    }
}

public sealed record RetainedSpeedSample(SpeedSample Sample, string RetentionReason);

public enum TelemetryRetentionProfile
{
    Compact = 0,
    Balanced = 1,
    Research = 2
}

public sealed class TelemetryRetentionOptions
{
    public int TargetSamples { get; init; }
    public int MaxSamples { get; init; }
    public double RelativeSpeedChangeThreshold { get; init; }
    public double MinAbsoluteSpeedChangeMBps { get; init; }
    public int ContextSamplesAroundEvents { get; init; }

    public static TelemetryRetentionOptions ForProfile(TelemetryRetentionProfile profile) => profile switch
    {
        TelemetryRetentionProfile.Compact => new TelemetryRetentionOptions
        {
            TargetSamples = 1_000,
            MaxSamples = 1_500,
            RelativeSpeedChangeThreshold = 0.03,
            MinAbsoluteSpeedChangeMBps = 1.0,
            ContextSamplesAroundEvents = 1
        },
        TelemetryRetentionProfile.Research => new TelemetryRetentionOptions
        {
            TargetSamples = 10_000,
            MaxSamples = 15_000,
            RelativeSpeedChangeThreshold = 0.01,
            MinAbsoluteSpeedChangeMBps = 0.5,
            ContextSamplesAroundEvents = 3
        },
        _ => new TelemetryRetentionOptions
        {
            TargetSamples = 3_000,
            MaxSamples = 5_000,
            RelativeSpeedChangeThreshold = 0.02,
            MinAbsoluteSpeedChangeMBps = 0.75,
            ContextSamplesAroundEvents = 2
        }
    };
}
