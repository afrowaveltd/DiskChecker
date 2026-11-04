using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class BackupScreenTests
{
    [Fact]
    public async Task WithoutSelectedDiskTheScreenAsksForOneAndGoesBack()
    {
        using var harness = TestHarness.Create();
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await new BackupScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("Není vybrán žádný disk"));
    }

    [Fact]
    public void ScreenExposesBackupIdentity()
    {
        var screen = new BackupScreen();

        Assert.Equal(ScreenIds.Backup, screen.Id);
        Assert.Equal("Nav.Backup", screen.TitleKey);
    }

    [Fact]
    public async Task ChoosingBackReturnsBack()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string source = BackupTestFixtures.CreateSourceImage(root);
        harness.Context.Selection.SelectedDisk = BackupTestFixtures.CreateDisk(source, BackupTestFixtures.SmallImageSize);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await new BackupScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("Zdrojový disk"));
    }

    [Fact]
    public async Task ChoosingRestoreNavigatesToTheRestoreScreen()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string source = BackupTestFixtures.CreateSourceImage(root);
        harness.Context.Selection.SelectedDisk = BackupTestFixtures.CreateDisk(source, BackupTestFixtures.SmallImageSize);
        harness.Io.EnqueueChoice(BackupScreen.ActionRestore);

        var result = await new BackupScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.Restore, result.Target);
    }

    [Fact]
    public async Task RawBackupWritesImageManifestAndNeverTouchesTheSource()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string source = BackupTestFixtures.CreateSourceImage(root);
        string target = BackupTestFixtures.NewDirectory(harness.Root, "target");
        string sourceHashBefore = BackupTestFixtures.Sha256OfFile(source);
        long sourceLengthBefore = new FileInfo(source).Length;

        harness.Context.Selection.SelectedDisk = BackupTestFixtures.CreateDisk(source, BackupTestFixtures.SmallImageSize);
        harness.Io.EnqueueChoice(BackupScreen.ActionTarget);
        harness.Io.EnqueueChoice(BackupScreen.ActionCustomTarget);
        harness.Io.EnqueueAsk(target);
        harness.Io.EnqueueChoice(BackupScreen.ActionStart);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await new BackupScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);

        string? backupRoot = BackupTestFixtures.FindChildDirectory(target, "DiskChecker_RawBackup_*");
        Assert.NotNull(backupRoot);
        string imagePath = Path.Combine(backupRoot!, "disk_image.img");
        Assert.True(File.Exists(imagePath));
        Assert.Equal(sourceLengthBefore, new FileInfo(imagePath).Length);

        var manifest = TuiBackupManifest.Read(Path.Combine(backupRoot!, "backup_manifest.json"));
        Assert.NotNull(manifest);
        Assert.Equal("RawImage", manifest!.Mode);
        Assert.Equal(sourceLengthBefore, manifest.TotalBytes);
        Assert.Equal(TuiImageBackupService.BlockSize, manifest.BlockSize);
        Assert.Equal(1, manifest.Parts);
        Assert.Contains("disk_image.img", manifest.PartFiles);
        Assert.False(string.IsNullOrWhiteSpace(manifest.MountableImage));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Sha256));
        Assert.NotNull(manifest.SourceModel);
        Assert.NotNull(manifest.SourceDrive);
        Assert.False(string.IsNullOrWhiteSpace(manifest.ImageFormat));
        Assert.False(string.IsNullOrWhiteSpace(manifest.BackupDate));
        Assert.Equal(0, manifest.UnreadableBytes);
        Assert.Equal(manifest.Sha256, BackupTestFixtures.Sha256OfFile(imagePath));

        // The source device is only ever opened read-only.
        Assert.Equal(sourceHashBefore, BackupTestFixtures.Sha256OfFile(source));
        Assert.Equal(sourceLengthBefore, new FileInfo(source).Length);
        Assert.True(harness.Io.Contains("Záloha dokončena"));
    }

    [Fact]
    public async Task VhdxBackupWritesAValidFixedImage()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string source = BackupTestFixtures.CreateSourceImage(root);
        string target = BackupTestFixtures.NewDirectory(harness.Root, "target");

        harness.Context.Selection.SelectedDisk = BackupTestFixtures.CreateDisk(source, BackupTestFixtures.SmallImageSize);
        harness.Io.EnqueueChoice(BackupScreen.ActionMode);
        harness.Io.EnqueueChoice(BackupScreen.ModeVhdx);
        harness.Io.EnqueueChoice(BackupScreen.ActionTarget);
        harness.Io.EnqueueChoice(BackupScreen.ActionCustomTarget);
        harness.Io.EnqueueAsk(target);
        harness.Io.EnqueueChoice(BackupScreen.ActionStart);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await new BackupScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);

        string? backupRoot = BackupTestFixtures.FindChildDirectory(target, "DiskChecker_VhdxBackup_*");
        Assert.NotNull(backupRoot);
        string imagePath = Path.Combine(backupRoot!, "disk_image.vhdx");
        Assert.True(File.Exists(imagePath));

        Assert.True(TuiVhdxImageUtility.TryReadInfo(imagePath, out var info));
        Assert.Equal(BackupTestFixtures.SmallImageSize, info.VirtualDiskSizeBytes);
        Assert.Equal(
            TuiVhdxImageUtility.CalculateRequiredFileBytes(BackupTestFixtures.SmallImageSize),
            new FileInfo(imagePath).Length);

        byte[] payload = BackupTestFixtures.ReadRegion(imagePath, info.DataStartOffset, (int)BackupTestFixtures.SmallImageSize);
        byte[] expected = BackupTestFixtures.ReadRegion(source, 0, (int)BackupTestFixtures.SmallImageSize);
        Assert.Equal(expected, payload);

        var manifest = TuiBackupManifest.Read(Path.Combine(backupRoot!, "backup_manifest.json"));
        Assert.NotNull(manifest);
        Assert.Equal("VhdxImage", manifest!.Mode);
        Assert.Equal(info.DataStartOffset, manifest.DataStartOffset);
    }

    [Fact]
    public async Task FileLevelBackupCopiesTheChosenDirectory()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string sourceFolder = BackupTestFixtures.CreateSourceFolder(Path.Combine(root, "documents"));
        string sourceDevice = BackupTestFixtures.CreateSourceImage(root);
        string target = BackupTestFixtures.NewDirectory(harness.Root, "target");

        harness.Context.Selection.SelectedDisk = BackupTestFixtures.CreateDisk(sourceDevice, BackupTestFixtures.SmallImageSize);
        harness.Io.EnqueueChoice(BackupScreen.ActionMode);
        harness.Io.EnqueueChoice(BackupScreen.ModeFile);
        harness.Io.EnqueueAsk(sourceFolder);
        harness.Io.EnqueueChoice(BackupScreen.ActionTarget);
        harness.Io.EnqueueChoice(BackupScreen.ActionCustomTarget);
        harness.Io.EnqueueAsk(target);
        harness.Io.EnqueueChoice(BackupScreen.ActionStart);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await new BackupScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);

        string? backupRoot = BackupTestFixtures.FindChildDirectory(target, "DiskChecker_Backup_*");
        Assert.NotNull(backupRoot);
        Assert.True(File.Exists(Path.Combine(backupRoot!, "file-0.txt")));
        Assert.True(File.Exists(Path.Combine(backupRoot!, "nested", "deep.txt")));

        var manifest = TuiBackupManifest.Read(Path.Combine(backupRoot!, "backup_manifest.json"));
        Assert.NotNull(manifest);
        Assert.Equal("FileLevel", manifest!.Mode);
        Assert.True(manifest.TotalBytes > 0);
    }

    [Fact]
    public async Task DecliningTheConfirmationWritesNothing()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string source = BackupTestFixtures.CreateSourceImage(root);
        string target = BackupTestFixtures.NewDirectory(harness.Root, "target");

        harness.Context.Selection.SelectedDisk = BackupTestFixtures.CreateDisk(source, BackupTestFixtures.SmallImageSize);
        harness.Io.EnqueueChoice(BackupScreen.ActionTarget);
        harness.Io.EnqueueChoice(BackupScreen.ActionCustomTarget);
        harness.Io.EnqueueAsk(target);
        harness.Io.EnqueueChoice(BackupScreen.ActionStart);
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await new BackupScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Null(BackupTestFixtures.FindChildDirectory(target, "DiskChecker_*Backup_*"));
    }

    [Fact]
    public void RequiredBytesFollowTheModeSpecificRules()
    {
        long source = 10L * 1024 * 1024 * 1024;

        Assert.Equal(source, TuiImageBackupService.GetRequiredBytes(source, TuiBackupMode.RawImage));
        Assert.Equal(source, TuiImageBackupService.GetRequiredBytes(source, TuiBackupMode.FileLevel));
        Assert.Equal(
            TuiVhdxImageUtility.CalculateRequiredFileBytes(source),
            TuiImageBackupService.GetRequiredBytes(source, TuiBackupMode.VhdxImage));
        Assert.Equal(0, TuiImageBackupService.GetRequiredBytes(0, TuiBackupMode.RawImage));
    }

    [Fact]
    public void TheSystemReserveIsKeptWhenDecidingWhetherATargetFits()
    {
        const long reserve = TuiImageBackupService.SystemReserveBytes;
        var tight = new TuiBackupTarget("/mnt/tight", "tight", 5L * 1024 * 1024 * 1024, 0);
        var roomy = new TuiBackupTarget("/mnt/roomy", "roomy", 100L * 1024 * 1024 * 1024, 0);

        Assert.False(tight.Fits(2L * 1024 * 1024 * 1024, reserve));
        Assert.True(roomy.Fits(2L * 1024 * 1024 * 1024, reserve));
        Assert.Equal(5L * 1024 * 1024 * 1024 - reserve, tight.UsableBytes(reserve));
    }

    [Fact]
    public void EnsureSpaceRefusesWhenTheReserveRuleWouldBeBroken()
    {
        string root = Path.GetTempPath();
        Assert.Throws<IOException>(() => TuiImageBackupService.EnsureSpace(root, long.MaxValue / 2));
    }

    [Fact]
    public async Task AMissingSourceDeviceIsReported()
    {
        string root = BackupTestFixtures.NewDirectory(Path.GetTempPath(), "dcheck-missing", Guid.NewGuid().ToString("N"));
        try
        {
            string target = BackupTestFixtures.NewDirectory(root, "target");
            var service = new TuiImageBackupService();

            await Assert.ThrowsAnyAsync<IOException>(async () =>
                await service.RunAsync(
                    new TuiBackupRequest
                    {
                        SourcePath = Path.Combine(root, "neexistuje.img"),
                        SourceModel = "broken",
                        TotalSize = 2L * 1024 * 1024,
                        Mode = TuiBackupMode.RawImage,
                        TargetRoot = target,
                    },
                    progress: null,
                    CancellationToken.None));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup.
            }
        }
    }
}
