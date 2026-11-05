using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Infrastructure.Hardware;
using Xunit;

namespace DiskChecker.Tests;

public sealed class SurfaceReadScannerTests
{
    [Fact]
    public async Task FullReadScansEveryByteWithoutChangingDiskImage()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] original = Enumerable.Range(0, 128 * 1024)
                .Select(i => (byte)(i % 251)).ToArray();
            await File.WriteAllBytesAsync(path, original);
            byte[] expectedHash = SHA256.HashData(original);
            var result = await SurfaceReadScanner.ScanAsync(path, original.Length, null);
            Assert.Equal(0, result.ErrorCount);
            Assert.True(result.FullDiskRead);
            Assert.Equal(original.Length, result.TotalBytesTested);
            Assert.True(result.Samples.Count > 0);
            Assert.Equal(100d, result.Samples[^1].ProgressPercent);
            Assert.Equal(expectedHash, SHA256.HashData(await File.ReadAllBytesAsync(path)));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PartialReadIsNotClassifiedAsFullDisk()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[32768]);
            var result = await SurfaceReadScanner.ScanAsync(path, 32768, 4096);
            Assert.Equal(0, result.ErrorCount);
            Assert.False(result.FullDiskRead);
            Assert.Equal(4096, result.TotalBytesTested);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PrematureEndOfDeviceCannotBeReportedAsSuccess()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[512]);
            var result = await SurfaceReadScanner.ScanAsync(path, 4096, null);
            Assert.True(result.ErrorCount > 0);
            Assert.Equal(512, result.TotalBytesTested);
            Assert.Contains("Unexpected end", result.Notes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CancellationIsNotReportedAsCompleted()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[4096]);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                SurfaceReadScanner.ScanAsync(path, 4096, null, cancellationToken: cts.Token));
        }
        finally { File.Delete(path); }
    }
}
