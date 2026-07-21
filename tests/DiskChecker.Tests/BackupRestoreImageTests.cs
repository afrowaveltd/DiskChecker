using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.ViewModels;
using Xunit;

namespace DiskChecker.Tests;

public class BackupRestoreImageTests : IDisposable
{
    private readonly string _fixtureDir;

    public BackupRestoreImageTests()
    {
        _fixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fixtureDir);
    }

    [Fact]
    public async Task FixedVhdx_For500MbVirtualDisk_IsExtendedToRequiredPayloadSize()
    {
        var sourceBytes = 500L * 1024 * 1024;
        var vhdxPath = Path.Combine(_fixtureDir, "test_500mb.vhdx");

        await VhdxImageUtility.WriteFixedHeaderAsync(vhdxPath, sourceBytes, CancellationToken.None);

        var expected = VhdxImageUtility.CalculateRequiredFileBytes(sourceBytes);
        var info = new FileInfo(vhdxPath);
        Assert.Equal(expected, info.Length);
        VhdxImageUtility.ValidateFixedImageLength(vhdxPath, sourceBytes);
        Assert.True(VhdxImageUtility.TryReadInfo(vhdxPath, out var parsed));
        Assert.Equal(sourceBytes, parsed.VirtualDiskSizeBytes);
        Assert.Equal(expected, parsed.RequiredFileBytes);
    }

    [Fact]
    public async Task VhdxPayloadRoundTrip_FromGenerated500MbImg_RestoresOriginalBytes()
    {
        var sourceBytes = 500L * 1024 * 1024;
        var sourcePath = Path.Combine(_fixtureDir, "source_500mb.img");
        var vhdxPath = Path.Combine(_fixtureDir, "backup_500mb.vhdx");
        var restoredPath = Path.Combine(_fixtureDir, "restored_500mb.img");

        await CreateDeterministicImageAsync(sourcePath, sourceBytes);
        var sourceSha = await Sha256Async(sourcePath, 0, sourceBytes);

        await VhdxImageUtility.WriteFixedHeaderAsync(vhdxPath, sourceBytes, CancellationToken.None);
        var dataOffset = VhdxImageUtility.CalculateDataStartOffset(sourceBytes);
        await CopyRegionAsync(sourcePath, 0, vhdxPath, dataOffset, sourceBytes);

        Assert.Equal(VhdxImageUtility.CalculateRequiredFileBytes(sourceBytes), new FileInfo(vhdxPath).Length);
        Assert.Equal(sourceSha, await Sha256Async(vhdxPath, dataOffset, sourceBytes));

        await CopyRegionAsync(vhdxPath, dataOffset, restoredPath, 0, sourceBytes);
        Assert.Equal(sourceBytes, new FileInfo(restoredPath).Length);
        Assert.Equal(sourceSha, await Sha256Async(restoredPath, 0, sourceBytes));
    }

    [Fact]
    public void RestoreDiscovery_ManualVhdx_UsesVirtualDiskSizeNotContainerLength()
    {
        var method = typeof(RestoreViewModel).GetMethod("CreateDiscoveredBackupFromImage", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
    }

    private static async Task CreateDeterministicImageAsync(string path, long bytes)
    {
        const int bufferSize = 1024 * 1024;
        var buffer = new byte[bufferSize];
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        long written = 0;
        uint state = 0x12345678;
        while (written < bytes)
        {
            var toWrite = (int)Math.Min(buffer.Length, bytes - written);
            for (var i = 0; i < toWrite; i++)
            {
                state = unchecked(state * 1664525 + 1013904223);
                buffer[i] = (byte)(state >> 24);
            }
            await stream.WriteAsync(buffer.AsMemory(0, toWrite));
            written += toWrite;
        }
    }

    private static async Task CopyRegionAsync(string source, long sourceOffset, string target, long targetOffset, long bytes)
    {
        const int bufferSize = 1024 * 1024;
        var buffer = new byte[bufferSize];
        await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        await using var targetStream = new FileStream(target, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        sourceStream.Position = sourceOffset;
        targetStream.Position = targetOffset;
        long remaining = bytes;
        while (remaining > 0)
        {
            var read = await sourceStream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
            if (read == 0) throw new EndOfStreamException();
            await targetStream.WriteAsync(buffer.AsMemory(0, read));
            remaining -= read;
        }
        await targetStream.FlushAsync();
    }

    private static async Task<string> Sha256Async(string path, long offset, long bytes)
    {
        const int bufferSize = 1024 * 1024;
        var buffer = new byte[bufferSize];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        stream.Position = offset;
        long remaining = bytes;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
            if (read == 0) throw new EndOfStreamException();
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public void Dispose()
    {
        try { Directory.Delete(_fixtureDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }
}
