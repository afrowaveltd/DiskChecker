using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class RestoreScreenTests
{
    private static RestoreScreen CreateScreen(string scanRoot)
    {
        var screen = new RestoreScreen();
        screen.ScanRoots = new[] { scanRoot };
        return screen;
    }

    [Fact]
    public void ScreenExposesRestoreIdentity()
    {
        var screen = new RestoreScreen();

        Assert.Equal(ScreenIds.Restore, screen.Id);
        Assert.Equal("Nav.Restore", screen.TitleKey);
    }

    [Fact]
    public async Task WithoutAnyBackupTheScreenSaysSoAndGoesBack()
    {
        using var harness = TestHarness.Create();
        string emptyRoot = BackupTestFixtures.NewDirectory(harness.Root, "empty");
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await CreateScreen(emptyRoot).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("nebyly nalezeny žádné zálohy"));
    }

    [Fact]
    public async Task RawBackupIsRestoredOntoTheChosenTarget()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string source = BackupTestFixtures.CreateSourceImage(root);
        string backupParent = BackupTestFixtures.NewDirectory(harness.Root, "backups");
        var backup = await BackupTestFixtures.CreateRawBackupAsync(backupParent, source, BackupTestFixtures.SmallImageSize);
        string targetFile = Path.Combine(BackupTestFixtures.NewDirectory(harness.Root, "restore"), "restored.img");

        harness.Io.EnqueueChoice(backup.BackupRoot);
        harness.Io.EnqueueChoice(RestoreScreen.ActionCustomTarget);
        harness.Io.EnqueueAsk(targetFile);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await CreateScreen(backupParent).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(File.Exists(targetFile));
        Assert.Equal(BackupTestFixtures.SmallImageSize, new FileInfo(targetFile).Length);
        Assert.Equal(BackupTestFixtures.Sha256OfFile(source), BackupTestFixtures.Sha256OfFile(targetFile));
        Assert.True(harness.Io.Contains("Obnova dokončena"));
    }

    [Fact]
    public async Task RestoreWritesNothingWhenTheFirstConfirmationIsDeclined()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string source = BackupTestFixtures.CreateSourceImage(root);
        string backupParent = BackupTestFixtures.NewDirectory(harness.Root, "backups");
        var backup = await BackupTestFixtures.CreateRawBackupAsync(backupParent, source, BackupTestFixtures.SmallImageSize);
        string targetFile = Path.Combine(BackupTestFixtures.NewDirectory(harness.Root, "restore"), "declined.img");

        harness.Io.EnqueueChoice(backup.BackupRoot);
        harness.Io.EnqueueChoice(RestoreScreen.ActionCustomTarget);
        harness.Io.EnqueueAsk(targetFile);
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await CreateScreen(backupParent).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(targetFile));
    }

    [Fact]
    public async Task RestoreWritesNothingWhenTheSecondConfirmationIsDeclined()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string source = BackupTestFixtures.CreateSourceImage(root);
        string backupParent = BackupTestFixtures.NewDirectory(harness.Root, "backups");
        var backup = await BackupTestFixtures.CreateRawBackupAsync(backupParent, source, BackupTestFixtures.SmallImageSize);
        string targetFile = Path.Combine(BackupTestFixtures.NewDirectory(harness.Root, "restore"), "declined-twice.img");

        harness.Io.EnqueueChoice(backup.BackupRoot);
        harness.Io.EnqueueChoice(RestoreScreen.ActionCustomTarget);
        harness.Io.EnqueueAsk(targetFile);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await CreateScreen(backupParent).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(targetFile));
    }

    [Fact]
    public async Task ManifestCanBeEnteredManually()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string source = BackupTestFixtures.CreateSourceImage(root);
        string backupParent = BackupTestFixtures.NewDirectory(harness.Root, "backups");
        var backup = await BackupTestFixtures.CreateRawBackupAsync(backupParent, source, BackupTestFixtures.SmallImageSize);
        string emptyRoot = BackupTestFixtures.NewDirectory(harness.Root, "empty");
        string targetFile = Path.Combine(BackupTestFixtures.NewDirectory(harness.Root, "restore"), "manual.img");

        harness.Io.EnqueueChoice(RestoreScreen.ActionManual);
        harness.Io.EnqueueAsk(backup.ManifestPath);
        harness.Io.EnqueueChoice(RestoreScreen.ActionCustomTarget);
        harness.Io.EnqueueAsk(targetFile);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await CreateScreen(emptyRoot).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(File.Exists(targetFile));
        Assert.Equal(BackupTestFixtures.Sha256OfFile(source), BackupTestFixtures.Sha256OfFile(targetFile));
    }

    [Fact]
    public async Task VhdxRestoreSkipsTheHeaderUsingTheManifestOffset()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string source = BackupTestFixtures.CreateSourceImage(root);
        string backupParent = BackupTestFixtures.NewDirectory(harness.Root, "backups");
        var backup = await BackupTestFixtures.CreateVhdxBackupAsync(backupParent, source, BackupTestFixtures.SmallImageSize);
        Assert.Equal(TuiBackupMode.VhdxImage, backup.Mode);

        var manifest = TuiBackupManifest.Read(backup.ManifestPath);
        Assert.NotNull(manifest);
        Assert.True(manifest!.DataStartOffset > 0);

        string targetFile = Path.Combine(BackupTestFixtures.NewDirectory(harness.Root, "restore"), "restored-vhdx.img");

        harness.Io.EnqueueChoice(backup.BackupRoot);
        harness.Io.EnqueueChoice(RestoreScreen.ActionCustomTarget);
        harness.Io.EnqueueAsk(targetFile);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        await CreateScreen(backupParent).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(targetFile));
        byte[] restored = BackupTestFixtures.ReadRegion(targetFile, 0, (int)BackupTestFixtures.SmallImageSize);
        byte[] expected = BackupTestFixtures.ReadRegion(source, 0, (int)BackupTestFixtures.SmallImageSize);
        Assert.Equal(expected, restored);
    }

    [Fact]
    public async Task FileLevelBackupIsReportedAsNotRestorable()
    {
        using var harness = TestHarness.Create();
        string root = BackupTestFixtures.NewDirectory(harness.Root, "fixtures");
        string sourceFolder = BackupTestFixtures.CreateSourceFolder(Path.Combine(root, "documents"));
        string backupParent = BackupTestFixtures.NewDirectory(harness.Root, "backups");

        var service = new TuiImageBackupService();
        var fileBackup = await service.RunAsync(
            new TuiBackupRequest
            {
                SourcePath = backupParent,
                SourceModel = "file level",
                Mode = TuiBackupMode.FileLevel,
                TargetRoot = backupParent,
                SourceDirectory = sourceFolder,
            },
            progress: null,
            CancellationToken.None);
        Assert.Equal(TuiBackupMode.FileLevel, fileBackup.Mode);

        harness.Io.EnqueueChoice(fileBackup.BackupRoot);
        harness.Io.EnqueueChoice(TuiKeys.Back);

        var result = await CreateScreen(backupParent).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("obnovit lze pouze raw"));
    }
}
