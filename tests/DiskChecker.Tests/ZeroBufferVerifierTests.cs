using DiskChecker.Infrastructure.Hardware.Sanitization;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Keep sanitization zero verification exact after switching from a
/// byte-by-byte scan to the runtime's vectorized clean-block fast path.
/// </summary>
public sealed class ZeroBufferVerifierTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(4 * 1024 * 1024)]
    public void CleanBufferReportsNoCorruption(int length)
    {
        var block = new byte[length];
        var (count, first) = ZeroBufferVerifier.Inspect(block);
        Assert.Equal(0, count);
        Assert.Equal(-1, first);
    }

    [Fact]
    public void CorruptBufferRetainsExactCountAndFirstOffset()
    {
        var block = new byte[8192];
        block[255] = 0x01;
        block[256] = 0xFF;
        block[8191] = 0x02;

        var (count, first) = ZeroBufferVerifier.Inspect(block);
        Assert.Equal(3, count);
        Assert.Equal(255, first);
    }

    [Fact]
    public void CleanSliceDoesNotInspectStaleBytesOutsideReadWindow()
    {
        var block = new byte[1024];
        block[700] = 0xA5;
        var (count, first) = ZeroBufferVerifier.Inspect(block.AsSpan(0, 512));
        Assert.Equal(0, count);
        Assert.Equal(-1, first);
    }

    [Fact]
    public void CorruptionAtFirstByteIsDetected()
    {
        var block = new byte[4096];
        block[0] = 42;
        var (count, first) = ZeroBufferVerifier.Inspect(block);
        Assert.Equal(1, count);
        Assert.Equal(0, first);
    }
}
