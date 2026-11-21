using DiskChecker.Core.Services;
using Xunit;

namespace DiskChecker.Tests;

public sealed class TelemetrySamplingTests
{
    [Theory]
    [InlineData(0, 1024, 1)]
    [InlineData(1024, 1024, 1)]
    [InlineData(1025, 1024, 2)]
    [InlineData(2048, 1024, 2)]
    [InlineData(2049, 1024, 3)]
    [InlineData(3700, 1024, 4)]
    [InlineData(65535, 1024, 64)]
    [InlineData(1000000, 1024, 977)]
    public void Stride_CoversTheWholeRangeWithoutExceedingThePointBudget(
        long count, int budget, int expected)
    {
        Assert.Equal(expected, TelemetrySampling.CalculateStride(count, budget));
        // Selection with a contiguous dataset must not leave a large tail
        // unrepresented (the original floor+LIMIT error).
        if (count > budget)
        {
            long selected = (count + expected - 1) / expected;
            Assert.True(selected <= budget);
            Assert.True((count - 1) / expected >= selected - 1);
        }
    }

    [Fact]
    public void Stride_UsesLongArithmeticWithoutOverflow()
    {
        Assert.Equal(int.MaxValue, TelemetrySampling.CalculateStride(long.MaxValue, 1024));
    }

    [Fact]
    public void Stride_RejectsInvalidLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TelemetrySampling.CalculateStride(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TelemetrySampling.CalculateStride(-1, 1));
    }
}
