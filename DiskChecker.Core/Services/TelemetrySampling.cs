namespace DiskChecker.Core.Services;

/// <summary>
/// Computes a whole-range stride for a fixed-size preview. Floor division plus
/// LIMIT truncates the *end* of larger tests (e.g. sanitizer progress ends at
/// 50-80%), which falsely presents a partial scan as a full chart.
/// </summary>
public static class TelemetrySampling
{
    /// <summary>
    /// Returns ceil(totalCount/maxPoints) without overflow, allowing an ordered
    /// SQL modulo sample to cover the whole recording instead of just its start.
    /// </summary>
    public static int CalculateStride(long totalCount, int maxPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPoints);
        if (totalCount <= maxPoints) return 1;

        long stride = 1 + (totalCount - 1) / maxPoints;
        return checked((int)Math.Min(stride, int.MaxValue));
    }
}
