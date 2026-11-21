using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Xunit;

namespace DiskChecker.Tui.Tests;

/// <summary>
/// Regression tests for fail-closed restores. All targets are disposable regular
/// files; no test opens a physical disk or needs administrator privileges.
/// </summary>
public sealed class TuiRestoreSafetyTests
{
    private const int ImageBytes = 4096;

    private static (string Image, string Target, string Checksum) Fixture(TestHarness harness)
    {
        string root = BackupTestFixtures.NewDirectory(harness.Root, "restore-safety");
        string image = BackupTestFixtures.CreateSourceImage(root, ImageBytes);
        string target = Path.Combine(root, "restored.img");
        return (image, target, BackupTestFixtures.Sha256OfFile(image));
    }

    private static TuiRestoreRequest Request(
        string image, string target, long totalBytes, string? checksum, long unreadableBytes = 0) => new()
    {
        ImagePaths = new[] { image },
        TargetPath = target,
        TotalBytes = totalBytes,
        ExpectedSha256 = checksum,
        UnreadableBytes = unreadableBytes,
    };

    [Fact]
    public async Task ValidManifestAndReadbackVerifyTheRestoredTarget()
    {
        using var harness = TestHarness.Create();
        var (image, target, checksum) = Fixture(harness);
        var result = await new TuiImageBackupService().RestoreAsync(
            Request(image, target, ImageBytes, checksum), null, CancellationToken.None);

        Assert.True(result.Verified);
        Assert.False(result.Partial);
        Assert.Equal(ImageBytes, result.BytesWritten);
        Assert.Equal(checksum, result.Sha256);
        Assert.Equal(checksum, BackupTestFixtures.Sha256OfFile(target));
    }

    [Fact]
    public async Task NoManifestHashDoesNotClaimVerifiedRecovery()
    {
        using var harness = TestHarness.Create();
        var (image, target, checksum) = Fixture(harness);

        var result = await new TuiImageBackupService().RestoreAsync(
            Request(image, target, ImageBytes, null), null, CancellationToken.None);

        Assert.False(result.Verified);
        Assert.False(result.Partial);
        Assert.Equal(checksum, BackupTestFixtures.Sha256OfFile(target));
    }

    [Fact]
    public async Task ImageWithUnreadableSourceSectorsIsAlwaysPartial()
    {
        using var harness = TestHarness.Create();
        var (image, target, checksum) = Fixture(harness);

        var result = await new TuiImageBackupService().RestoreAsync(
            Request(image, target, ImageBytes, checksum, unreadableBytes: 512),
            null, CancellationToken.None);

        Assert.False(result.Verified);
        Assert.True(result.Partial);
        Assert.Equal(checksum, BackupTestFixtures.Sha256OfFile(target));
    }

    [Fact]
    public async Task WrongManifestDigestLeavesExistingTargetUntouched()
    {
        using var harness = TestHarness.Create();
        var (image, target, _) = Fixture(harness);
        File.WriteAllText(target, "original target");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new TuiImageBackupService().RestoreAsync(
                Request(image, target, ImageBytes, new string('0', 64)),
                null, CancellationToken.None));

        Assert.Equal("original target", File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.restore-*.tmp"));
    }

    [Fact]
    public async Task FaultInjectedReadbackCannotBeReportedAsVerified()
    {
        using var harness = TestHarness.Create();
        var (image, target, checksum) = Fixture(harness);
        File.WriteAllText(target, "unchanged");

        var service = new TuiImageBackupService
        {
            ReadbackStreamFactory = _ => new MemoryStream(new byte[ImageBytes], writable: false),
        };

        await Assert.ThrowsAsync<IOException>(() =>
            service.RestoreAsync(Request(image, target, ImageBytes, checksum),
                null, CancellationToken.None));

        Assert.Equal("unchanged", File.ReadAllText(target));
    }

    [Fact]
    public async Task MissingImagePartIsRejectedBeforeTouchingTarget()
    {
        using var harness = TestHarness.Create();
        var (image, target, checksum) = Fixture(harness);
        File.WriteAllText(target, "unchanged");

        var request = new TuiRestoreRequest
        {
            ImagePaths = new[] { image, Path.Combine(harness.Root, "missing.img") },
            TargetPath = target,
            TotalBytes = ImageBytes,
            ExpectedSha256 = checksum,
        };

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            new TuiImageBackupService().RestoreAsync(request, null, CancellationToken.None));

        Assert.Equal("unchanged", File.ReadAllText(target));
    }

    [Fact]
    public async Task TruncatedImageIsRejectedBeforeTouchingTarget()
    {
        using var harness = TestHarness.Create();
        var (image, target, checksum) = Fixture(harness);
        File.WriteAllText(target, "unchanged");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new TuiImageBackupService().RestoreAsync(
                Request(image, target, ImageBytes * 2, checksum),
                null, CancellationToken.None));

        Assert.Equal("unchanged", File.ReadAllText(target));
    }

    [Fact]
    public async Task ImageCannotBeOverwrittenByItsOwnRestore()
    {
        using var harness = TestHarness.Create();
        var (image, _, checksum) = Fixture(harness);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TuiImageBackupService().RestoreAsync(
                Request(image, image, ImageBytes, checksum),
                null, CancellationToken.None));

        Assert.Equal(checksum, BackupTestFixtures.Sha256OfFile(image));
    }

    [Fact]
    public async Task PhysicalDeviceRestoreIsDisabledPendingIdentityValidation()
    {
        using var harness = TestHarness.Create();
        var (image, _, checksum) = Fixture(harness);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new TuiImageBackupService().RestoreAsync(
                Request(image, "/dev/sda", ImageBytes, checksum),
                null, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationPreservesExistingTarget()
    {
        using var harness = TestHarness.Create();
        var (image, target, checksum) = Fixture(harness);
        File.WriteAllText(target, "unchanged");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TuiImageBackupService().RestoreAsync(
                Request(image, target, ImageBytes, checksum),
                null, new CancellationToken(canceled: true)));

        Assert.Equal("unchanged", File.ReadAllText(target));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.restore-*.tmp"));
    }
}
