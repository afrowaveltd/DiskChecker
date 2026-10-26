using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

[Collection("Seek UI")]
public sealed class BackupWorkflowUiTests(SeekUiFixture fixture) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("DiskChecker-backup-").FullName;

    [Fact]
    public Task SafeWorkflowVhdxIsFlushedValidatedAndVerifiedBeforeSuccess()
        => fixture.Session.Dispatch(async () =>
        {
            var data = await CreateSourceAsync();
            var source = Path.Combine(_directory, "source.img");
            using var vm = new SafeDestructiveTestViewModel(Substitute.For<INavigationService>(),
                Substitute.For<ISelectedDiskService>(), Substitute.For<IDialogService>(),
                Substitute.For<IDiskSanitizationService>(), null!, Substitute.For<ISmartaProvider>(), null!,
                Substitute.For<IDiskCardRepository>(), Substitute.For<ICertificateGenerator>(), null!,
                Substitute.For<IPowerManagementService>(), Substitute.For<IDiskTemperatureMonitor>())
            {
                SelectedDrive = new CoreDriveInfo { Path = source, TotalSize = data.Length },
                DiskPath = source, DiskTotalBytes = data.Length,
                BackupTargetPath = _directory, BackupTargetFreeBytes = 100L * 1024 * 1024
            };

            var method = typeof(SafeDestructiveTestViewModel).GetMethod("RunVhdxBackupPhaseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(vm, new object[] { CancellationToken.None })!;

            Assert.True(vm.VhdxBackupVerified);
            AssertPayload(vm.VhdxBackupPath, data);
            var manifestPath = Directory.GetFiles(_directory, "backup_manifest.json", SearchOption.AllDirectories).Single();
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            Assert.Equal(data.Length, manifest.RootElement.GetProperty("VirtualDiskBytes").GetInt64());
            return true;
        }, CancellationToken.None);

    [Fact]
    public Task RawConversionPreservesPayloadAndExactVirtualSize()
        => fixture.Session.Dispatch(async () =>
        {
            var data = await CreateSourceAsync();
            var source = Path.Combine(_directory, "source.img");
            var manifestPath = Path.Combine(_directory, "backup_manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
            { Mode = "RawImage", TotalBytes = data.Length, PartPaths = new[] { source } }));
            using var vm = new BackupViewModel(Substitute.For<INavigationService>(), Substitute.For<ISelectedDiskService>(),
                Substitute.For<IDialogService>(), Substitute.For<ISmartaProvider>());

            var method = typeof(BackupViewModel).GetMethod("ConvertRawManifestToVhdxAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(vm, new object[] { manifestPath, CancellationToken.None })!;

            var image = Path.Combine(_directory, "disk_image_converted.vhdx");
            AssertPayload(image, data);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "backup_manifest_converted_vhdx.json")));
            Assert.Equal(data.Length, manifest.RootElement.GetProperty("VirtualDiskBytes").GetInt64());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), manifest.RootElement.GetProperty("Sha256").GetString());
            return true;
        }, CancellationToken.None);

    private async Task<byte[]> CreateSourceAsync()
    {
        var data = new byte[1024 * 1024 + 512];
        new Random(42).NextBytes(data);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "source.img"), data);
        return data;
    }

    private static void AssertPayload(string path, byte[] expected)
    {
        Assert.True(VhdxImageUtility.TryReadInfo(path, out var info));
        Assert.Equal(expected.Length, info.VirtualDiskSizeBytes);
        using var stream = File.OpenRead(path);
        stream.Position = info.DataStartOffset;
        var actual = new byte[expected.Length];
        stream.ReadExactly(actual);
        Assert.Equal(SHA256.HashData(expected), SHA256.HashData(actual));
    }

    public void Dispose()
    {
        foreach (var path in Directory.GetFiles(_directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(_directory, recursive: true);
    }
}
