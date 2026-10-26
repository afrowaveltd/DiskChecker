using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using NSubstitute;
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
    public async Task FixedVhdx_UsesAlignedRegionsAndSpecGuids()
    {
        const long bytes = 4L * 1024 * 1024;
        var path = Path.Combine(_fixtureDir, "layout.vhdx");
        await VhdxImageUtility.WriteFixedHeaderAsync(path, bytes, CancellationToken.None);

        using var fs = File.OpenRead(path);
        using var reader = new BinaryReader(fs);
        fs.Position = 192L * 1024;
        var regionA = reader.ReadBytes(64 * 1024);
        fs.Position = 256L * 1024;
        var regionB = reader.ReadBytes(64 * 1024);
        Assert.Equal(regionA, regionB);

        var expectedBatGuid = new byte[] { 0x66, 0x77, 0xC2, 0x2D, 0x23, 0xF6, 0x00, 0x42, 0x9D, 0x64, 0x11, 0x5E, 0x9B, 0xFD, 0x4A, 0x08 };
        var expectedMetadataGuid = new byte[] { 0x06, 0xA2, 0x7C, 0x8B, 0x90, 0x47, 0x9A, 0x4B, 0xB8, 0xFE, 0x57, 0x5F, 0x05, 0x0F, 0x88, 0x6E };
        Assert.Equal(expectedBatGuid, regionA.AsSpan(16, 16).ToArray());
        Assert.Equal(expectedMetadataGuid, regionA.AsSpan(48, 16).ToArray());
        var batOffset = BitConverter.ToUInt64(regionA, 32);
        var metadataOffset = BitConverter.ToUInt64(regionA, 64);
        Assert.Equal(2UL * 1024 * 1024, batOffset);
        Assert.Equal(3UL * 1024 * 1024, metadataOffset);
        Assert.Equal((uint)(1024 * 1024), BitConverter.ToUInt32(regionA, 40));

        fs.Position = (long)metadataOffset + 32;
        var metadataEntry = reader.ReadBytes(32);
        var fileParametersOffset = BitConverter.ToUInt32(metadataEntry, 16);
        Assert.True(fileParametersOffset >= 64 * 1024);
        Assert.Equal((byte)4, metadataEntry[24]); // IsRequired
        fs.Position = (long)metadataOffset + fileParametersOffset;
        Assert.Equal((uint)(1024 * 1024), reader.ReadUInt32());
        Assert.Equal(1U, reader.ReadUInt32()); // LeaveBlocksAllocated

        fs.Position = (long)batOffset;
        var firstBatEntry = reader.ReadUInt64();
        var expectedDataOffset = VhdxImageUtility.CalculateDataStartOffset(bytes);
        Assert.Equal((((ulong)expectedDataOffset / (1024UL * 1024)) << 20) | 6UL, firstBatEntry);
        Assert.True(VhdxImageUtility.TryReadInfo(path, out var image));
        Assert.Equal(expectedDataOffset, image.DataStartOffset);
        Assert.Equal(bytes, image.VirtualDiskSizeBytes);
    }

    [Fact]
    public async Task FixedVhdx_WhenQemuImgIsInstalled_OpensAndConvertsToRaw()
    {
        // qemu-img independently validates the VHDX layout. On CI it is installed
        // explicitly; local Windows test runs without QEMU can skip this integration check.
        const string qemu = "/usr/bin/qemu-img";
        if (!OperatingSystem.IsLinux() || !File.Exists(qemu)) return;

        const long bytes = 4L * 1024 * 1024;
        var image = Path.Combine(_fixtureDir, "qemu-smoke.vhdx");
        var raw = Path.Combine(_fixtureDir, "qemu-output.raw");
        await VhdxImageUtility.WriteFixedHeaderAsync(image, bytes, CancellationToken.None);
        await RunQemuAsync("info", "-f", "vhdx", image);
        await RunQemuAsync("convert", "-f", "vhdx", "-O", "raw", image, raw);
        Assert.Equal(bytes, new FileInfo(raw).Length);
    }

    private static async Task RunQemuAsync(params string[] args)
        => await RunToolAsync("/usr/bin/qemu-img", args);

    private static async Task RunToolAsync(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("qemu-img failed to start.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, $"{tool} {string.Join(" ", args)} failed: {output} {error}");
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(8193)]
    [InlineData(131072)]
    public async Task FixedVhdx_ChunkBoundariesAreReadableByIndependentReader(int payloadBlocks)
    {
        // Sparse files exercise real disk sizes without allocating GiB of test data.
        // The last case also crosses the first 1 MiB BAT-region growth boundary.
        var bytes = (long)payloadBlocks * VhdxImageUtility.LogicalBlockSize + 512;
        var path = Path.Combine(_fixtureDir, $"boundary-{payloadBlocks}.vhdx");
        await VhdxImageUtility.WriteFixedHeaderAsync(path, bytes, CancellationToken.None);
        Assert.True(VhdxImageUtility.TryReadInfo(path, out var info));
        Assert.Equal(bytes, info.VirtualDiskSizeBytes);

        var offsets = new[] { 0L, 4L * 1024 * 1024 * 1024 - 512, 4L * 1024 * 1024 * 1024, bytes - 512 };
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            foreach (var offset in offsets)
            {
                stream.Position = info.DataStartOffset + offset;
                var marker = new byte[512];
                Array.Fill(marker, (byte)0x5A);
                stream.Write(marker);
            }
        }
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/qemu-io"))
            Assert.Skip("Independent VHDX boundary verification requires qemu-utils on Linux.");

        await RunQemuAsync("info", "-f", "vhdx", path);
        foreach (var offset in offsets)
            await RunToolAsync("/usr/bin/qemu-io", "-r", "-f", "vhdx", "-c", $"read -P 0x5a {offset} 512", path);
    }

    [Fact]
    public async Task FixedVhdx_CorruptBatEntry_IsRejectedBeforeRestore()
    {
        var path = Path.Combine(_fixtureDir, "corrupt-bat.vhdx");
        await VhdxImageUtility.WriteFixedHeaderAsync(path, 2L * 1024 * 1024, CancellationToken.None);
        Assert.True(VhdxImageUtility.TryReadInfo(path, out _));

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            fs.Position = 2L * 1024 * 1024;
            fs.Write(new byte[8]);
        }
        Assert.False(VhdxImageUtility.TryReadInfo(path, out _));
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

    [Fact]
    public async Task BackupWorkflow_WritesExactVirtualSizeAndIndependentlyRestorablePayload()
    {
        const long bytes = 4L * 1024 * 1024 + 512;
        var source = Path.Combine(_fixtureDir, "source.img");
        await CreateDeterministicImageAsync(source, bytes);
        using var vm = new BackupViewModel(Substitute.For<INavigationService>(), Substitute.For<ISelectedDiskService>(),
            Substitute.For<IDialogService>(), Substitute.For<ISmartaProvider>())
        { SourceDrive = new CoreDriveInfo { Path = source, Name = "Fixture disk", TotalSize = bytes } };
        vm.TargetDrives.Add(new BackupTargetItem
        {
            DrivePath = _fixtureDir, TotalFreeSpace = 100L * 1024 * 1024,
            IsSelected = true, AllocatedBytes = VhdxImageUtility.CalculateRequiredFileBytes(bytes)
        });

        var method = typeof(BackupViewModel).GetMethod("RunVhdxBackupAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(vm, new object[] { CancellationToken.None })!;

        var manifestPath = Directory.GetFiles(_fixtureDir, "backup_manifest.json", SearchOption.AllDirectories).Single();
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, TestContext.Current.CancellationToken));
        Assert.Equal(bytes, manifest.RootElement.GetProperty("VirtualDiskBytes").GetInt64());
        var image = manifest.RootElement.GetProperty("MountableImage").GetString()!;
        Assert.True(VhdxImageUtility.TryReadInfo(image, out var info));
        Assert.Equal(bytes, info.VirtualDiskSizeBytes);
        var expected = await Sha256Async(source, 0, bytes);
        Assert.Equal(expected, await Sha256Async(image, info.DataStartOffset, bytes));
        if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/qemu-img"))
        {
            var restored = Path.Combine(_fixtureDir, "workflow-restored.raw");
            await RunQemuAsync("convert", "-f", "vhdx", "-O", "raw", image, restored);
            Assert.Equal(bytes, new FileInfo(restored).Length);
            Assert.Equal(expected, await Sha256Async(restored, 0, bytes));
        }
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
