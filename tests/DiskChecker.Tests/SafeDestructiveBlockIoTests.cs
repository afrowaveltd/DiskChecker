using System;
using System.IO;
using System.Threading.Tasks;
using DiskChecker.UI.Avalonia.Services;
using Xunit;

namespace DiskChecker.Tests;

public sealed class SafeDestructiveBlockIoTests
{
    [Theory]
    [InlineData(512, 1)]
    [InlineData(262144, 1)]
    [InlineData(262656, 2)]
    public void BlockCountIncludesRemainder(long length, long expected)
        => Assert.Equal(expected, SafeDestructiveBlockIo.GetBlockCount(length, 262144));

    [Fact]
    public async Task TailWriteAndVerificationDoNotTouchBytesOutsideRequestedRange()
    {
        var ct = TestContext.Current.CancellationToken;
        var data = new byte[1024];
        Array.Fill(data, (byte)0xAB);
        using var stream = new MemoryStream(data);
        var buffer = new byte[262144];
        await SafeDestructiveBlockIo.TransferAsync(stream, buffer, 512, true, 0, ct);
        Assert.Equal(512L, stream.Position);
        Assert.All(data[..512], b => Assert.Equal((byte)0, b));
        Assert.All(data[512..], b => Assert.Equal((byte)0xAB, b));
        stream.Position = 0;
        await SafeDestructiveBlockIo.TransferAsync(stream, buffer, 512, false, 0, ct);
    }

    [Fact]
    public async Task NonzeroDataAndShortReadsAreFailures()
    {
        var ct = TestContext.Current.CancellationToken;
        using var corrupt = new MemoryStream(new byte[] { 0, 0, 9, 0 });
        var error = await Assert.ThrowsAsync<IOException>(() =>
            SafeDestructiveBlockIo.TransferAsync(corrupt, new byte[8], 4, false, 1024, ct));
        Assert.Contains("1026", error.Message);
        using var shortStream = new MemoryStream(new byte[2]);
        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            SafeDestructiveBlockIo.TransferAsync(shortStream, new byte[8], 4, false, 0, ct));
    }
}
