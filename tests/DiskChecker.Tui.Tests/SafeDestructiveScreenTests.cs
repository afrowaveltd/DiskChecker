using System.Globalization;
using System.Security.Cryptography;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DiskChecker.Tui.Tests;

/// <summary>
/// Covers the safe destructive test screen and its streaming workflow service:
/// the raw image round-trip, the verification gate, the "backup only" mode, the
/// emergency restore and the persisted session/certificate.
/// </summary>
public sealed class SafeDestructiveScreenTests
{
    // ──────────────────────────────────────────────
    //  Screen level
    // ──────────────────────────────────────────────

    [Fact]
    public async Task MissingDiskShowsWarningAndGoesBack()
    {
        using var harness = CreateHarness();

        var result = await new SafeDestructiveScreen().ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Contains("vyberte disk", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmationRefusalLeavesDiskAndTargetUntouched()
    {
        using var workspace = new Workspace();
        var sanitization = new FakeSanitizationService();
        using var harness = CreateHarness(workspace, sanitization);
        harness.Context.Selection.SelectedDisk = workspace.CreateDisk();

        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice("back");

        var result = await CreateScreen(workspace).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Contains("Záloha bude uložena do", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.EnumerateFileSystemEntries(workspace.TargetDirectory).Any());
        Assert.Equal(workspace.Content, File.ReadAllBytes(workspace.SourcePath));
        Assert.Equal(0, sanitization.SanitizeCalls);
    }

    [Fact]
    public async Task RawModeBacksUpTestsAndRestoresTheDisk()
    {
        using var workspace = new Workspace();
        var sanitization = new FakeSanitizationService();
        using var harness = CreateHarness(workspace, sanitization);
        harness.Context.Selection.SelectedDisk = workspace.CreateDisk();

        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice("back");

        var result = await CreateScreen(workspace).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Equal(1, sanitization.SanitizeCalls);

        // The source disk was overwritten by the test and restored from the image.
        Assert.Equal(workspace.Content, File.ReadAllBytes(workspace.SourcePath));

        string image = workspace.SingleImagePath();
        Assert.Equal(workspace.Content, File.ReadAllBytes(image));
        Assert.True(File.Exists(Path.Combine(workspace.SingleImageDirectory(), TuiSafeWorkflowService.ManifestFileName)));

        Assert.NotNull(harness.Io.LastProgress);
        Assert.Equal(100d, harness.Io.LastProgress!.Percent);
        Assert.Contains("Známka", harness.Io.Output, StringComparison.Ordinal);
        Assert.False(harness.Context.NavigationLocked);
    }

    [Fact]
    public async Task BackupOnlyModeCreatesVerifiedImageWithoutDestructivePhase()
    {
        using var workspace = new Workspace();
        var sanitization = new FakeSanitizationService();
        using var harness = CreateHarness(workspace, sanitization);
        harness.Context.Selection.SelectedDisk = workspace.CreateDisk();

        harness.Io.EnqueueChoice("mode");
        harness.Io.EnqueueChoice("backup-only");
        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice("back");

        var result = await CreateScreen(workspace).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Equal(0, sanitization.SanitizeCalls);
        Assert.Equal(workspace.Content, File.ReadAllBytes(workspace.SourcePath));

        string image = workspace.SingleImagePath();
        Assert.Equal(workspace.Content, File.ReadAllBytes(image));
        Assert.Contains("Pouze záloha", harness.Io.Output, StringComparison.Ordinal);
        Assert.Contains("přeskočeno", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnreadableBackupBlocksTheDestructivePhaseAndKeepsTheImage()
    {
        using var workspace = new Workspace();
        var sanitization = new FakeSanitizationService();
        using var harness = CreateHarness(workspace, sanitization);
        harness.Context.Selection.SelectedDisk = workspace.CreateDisk();

        var screen = CreateScreen(workspace);
        screen.Workflow.SourceStreamFactory = _ => new FailingBlockStream(workspace.Content, 1);

        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice("back");

        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(0, sanitization.SanitizeCalls);
        Assert.Contains("nečitelných", harness.Io.Output, StringComparison.OrdinalIgnoreCase);

        // The rescue copy stays on disk even though the test was refused.
        string image = workspace.SingleImagePath();
        Assert.Equal(workspace.Content.Length, new FileInfo(image).Length);
    }

    [Fact]
    public async Task DestructiveFailureTriggersEmergencyRestore()
    {
        using var workspace = new Workspace();
        var sanitization = new CorruptingSanitizationService(workspace.SourcePath);
        using var harness = CreateHarness(workspace, sanitization);
        harness.Context.Selection.SelectedDisk = workspace.CreateDisk();

        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice("back");

        var result = await CreateScreen(workspace).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Equal(1, sanitization.SanitizeCalls);
        Assert.Contains("Nouzová obnova", harness.Io.Output, StringComparison.OrdinalIgnoreCase);

        // The corrupted disk holds the original bytes again.
        Assert.Equal(workspace.Content, File.ReadAllBytes(workspace.SourcePath));
        Assert.Contains("simulované selhání", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionAndCertificateArePersisted()
    {
        using var workspace = new Workspace();
        using var harness = CreateHarness(workspace, new FakeSanitizationService());
        harness.Context.Selection.SelectedDisk = workspace.CreateDisk();

        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice("back");

        await CreateScreen(workspace).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        using var scope = harness.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDiskCardRepository>();

        var card = await repository.GetByDevicePathAsync(workspace.SourcePath);
        Assert.NotNull(card);

        var sessions = await repository.GetTestSessionsAsync(card!.Id);
        var session = Assert.Single(sessions);
        Assert.Equal(TestType.AbsoluteDestructive, session.TestType);
        Assert.Equal(TestStatus.Completed, session.Status);
        Assert.True(session.IsDestructive);
        Assert.Equal("C", session.Grade);
        Assert.Equal(70d, session.Score);
        Assert.Contains("Bezpečný destruktivní test", session.Notes, StringComparison.Ordinal);

        var certificates = await repository.GetCertificatesAsync(card.Id);
        var certificate = Assert.Single(certificates);
        Assert.StartsWith("SAFE-", certificate.CertificateNumber, StringComparison.Ordinal);
        Assert.Equal(24, certificate.CertificateNumber.Length);
        Assert.Contains(
            DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            certificate.CertificateNumber,
            StringComparison.Ordinal);
        Assert.Equal("C", certificate.Grade);
        Assert.Equal(70d, certificate.Score);
        Assert.Equal(session.Id, certificate.TestSessionId);
    }

    [Fact]
    public async Task CertificateActionNavigatesToCertificateScreen()
    {
        using var workspace = new Workspace();
        using var harness = CreateHarness(workspace, new FakeSanitizationService());
        harness.Context.Selection.SelectedDisk = workspace.CreateDisk();

        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice("certificate");

        var result = await CreateScreen(workspace).ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Navigate, result.Action);
        Assert.Equal(ScreenIds.Certificate, result.Target);
        Assert.NotNull(harness.Context.Selection.SelectedCertificateId);
    }

    // ──────────────────────────────────────────────
    //  Workflow service
    // ──────────────────────────────────────────────

    [Fact]
    public async Task BackupWritesManifestAndRestoreRoundTripsTemporaryFile()
    {
        using var workspace = new Workspace();
        var workflow = new TuiSafeWorkflowService();
        var token = TestContext.Current.CancellationToken;

        var backup = await workflow.CreateBackupAsync(
            workspace.SourcePath,
            workspace.TargetDirectory,
            workspace.Content.Length,
            "TestDisk 500GB",
            progress: null,
            token);

        Assert.Equal(TuiSafeWorkflowService.ImageFileName, Path.GetFileName(backup.ImagePath));
        Assert.StartsWith(
            TuiSafeWorkflowService.BackupFolderPrefix,
            Path.GetFileName(Path.GetDirectoryName(backup.ImagePath)),
            StringComparison.Ordinal);
        Assert.Equal(workspace.Content, File.ReadAllBytes(backup.ImagePath));
        Assert.Equal(workspace.Content.Length, backup.TotalBytes);
        Assert.Equal(0, backup.UnreadableBytes);
        Assert.True(backup.CanRunDestructivePhase);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(workspace.Content)), backup.Sha256);

        var manifest = await TuiSafeWorkflowService.ReadManifestAsync(backup.ManifestPath, token);
        Assert.NotNull(manifest);
        Assert.Equal(workspace.SourcePath, manifest!.SourceDrive);
        Assert.Equal("TestDisk 500GB", manifest.SourceModel);
        Assert.Equal("RawImage", manifest.Mode);
        Assert.Equal(TuiSafeWorkflowService.BlockSize, manifest.BlockSize);
        Assert.Equal(workspace.Content.Length, manifest.TotalBytes);
        Assert.Equal(0, manifest.UnreadableBytes);
        Assert.Equal(backup.Sha256, manifest.Sha256);
        Assert.True(DateTime.TryParse(manifest.BackupDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));

        Assert.True(await workflow.VerifyImageAsync(backup.ImagePath, backup.Sha256, backup.TotalBytes, null, token));
        Assert.False(await workflow.VerifyImageAsync(backup.ImagePath, new string('0', 64), backup.TotalBytes, null, token));

        // Damage the "disk" and restore it from the verified image.
        File.WriteAllBytes(workspace.SourcePath, new byte[workspace.Content.Length]);
        var restored = await workflow.RestoreAsync(backup.ImagePath, workspace.SourcePath, backup.TotalBytes, null, token);

        Assert.Equal(workspace.Content.Length, restored.BytesWritten);
        Assert.Equal(workspace.Content, File.ReadAllBytes(workspace.SourcePath));
        Assert.True(await workflow.VerifyRestoreAsync(workspace.SourcePath, backup.TotalBytes, backup.Sha256, null, token));
    }

    [Fact]
    public async Task UnreadableBlocksAreZeroFilledAndCounted()
    {
        using var workspace = new Workspace();
        var workflow = new TuiSafeWorkflowService
        {
            SourceStreamFactory = _ => new FailingBlockStream(workspace.Content, 1, 2),
        };

        var backup = await workflow.CreateBackupAsync(
            workspace.SourcePath,
            workspace.TargetDirectory,
            workspace.Content.Length,
            "TestDisk",
            progress: null,
            TestContext.Current.CancellationToken);

        const int block = TuiSafeWorkflowService.BlockSize;
        Assert.Equal(2 * (long)block, backup.UnreadableBytes);
        Assert.False(backup.CanRunDestructivePhase);

        var image = File.ReadAllBytes(backup.ImagePath);
        Assert.Equal(workspace.Content.Length, image.Length);
        Assert.Equal(workspace.Content[..block], image[..block]);
        Assert.All(image.AsSpan(block, block).ToArray(), value => Assert.Equal(0, value));
        Assert.All(image.AsSpan(2 * block, block).ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(workspace.Content[(3 * block)..], image[(3 * block)..]);

        // The manifest hash describes the image that was actually written.
        Assert.Equal(Convert.ToHexString(SHA256.HashData(image)), backup.Sha256);

        var manifest = await TuiSafeWorkflowService.ReadManifestAsync(backup.ManifestPath, TestContext.Current.CancellationToken);
        Assert.NotNull(manifest);
        Assert.Equal(2 * (long)block, manifest!.UnreadableBytes);
    }

    [Fact]
    public async Task SixtyFourConsecutiveUnreadableBlocksAbortTheBackup()
    {
        using var workspace = new Workspace();
        var failingBlocks = Enumerable.Range(0, TuiSafeWorkflowService.MaxConsecutiveUnreadableBlocks + 8).ToArray();
        var workflow = new TuiSafeWorkflowService
        {
            SourceStreamFactory = _ => new FailingBlockStream(workspace.Content, failingBlocks),
        };

        long requested = (long)TuiSafeWorkflowService.MaxConsecutiveUnreadableBlocks * TuiSafeWorkflowService.BlockSize;

        var exception = await Assert.ThrowsAsync<IOException>(() => workflow.CreateBackupAsync(
            workspace.SourcePath,
            workspace.TargetDirectory,
            requested,
            "TestDisk",
            progress: null,
            TestContext.Current.CancellationToken));

        Assert.Contains(
            TuiSafeWorkflowService.MaxConsecutiveUnreadableBlocks.ToString(CultureInfo.InvariantCulture),
            exception.Message,
            StringComparison.Ordinal);

        // The aborted run never publishes a manifest, so the partial image cannot be trusted.
        Assert.Empty(Directory.EnumerateFiles(workspace.TargetDirectory, TuiSafeWorkflowService.ManifestFileName, SearchOption.AllDirectories));
    }

    [Fact]
    public async Task BackupNeverModifiesTheSourceFile()
    {
        using var workspace = new Workspace();
        var workflow = new TuiSafeWorkflowService();
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workspace.SourcePath)));
        DateTime writtenAt = File.GetLastWriteTimeUtc(workspace.SourcePath);

        var backup = await workflow.CreateBackupAsync(
            workspace.SourcePath,
            workspace.TargetDirectory,
            workspace.Content.Length,
            "TestDisk",
            progress: null,
            TestContext.Current.CancellationToken);

        string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workspace.SourcePath)));
        Assert.Equal(before, after);
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(workspace.SourcePath));
        Assert.Equal(workspace.Content, File.ReadAllBytes(backup.ImagePath));
        Assert.Equal(before, backup.Sha256);
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    private static TestHarness CreateHarness(
        Workspace? workspace = null,
        IDiskSanitizationService? sanitization = null,
        FakeSmartaProvider? provider = null,
        FakeSeekExecutor? seekExecutor = null)
    {
        var smartaProvider = provider ?? new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        var sanitize = sanitization ?? new FakeSanitizationService();
        var executor = seekExecutor ?? new FakeSeekExecutor();

        var harness = TestHarness.Create(configure: services =>
        {
            services.RemoveAll<ISmartaProvider>();
            services.AddSingleton<ISmartaProvider>(smartaProvider);
            services.RemoveAll<IDiskSanitizationService>();
            services.AddSingleton(sanitize);
            services.RemoveAll<ISeekTestExecutor>();
            services.AddSingleton<ISeekTestExecutor>(executor);
        });

        if (workspace is not null)
        {
            // Certificate generation needs a writable base path; keep it inside the
            // throwaway workspace instead of the real user profile.
            string certificates = Path.Combine(workspace.Root, "certificates");
            Directory.CreateDirectory(certificates);
            harness.GetRequiredService<ISettingsService>()
                .SetCertificatePathAsync(certificates)
                .GetAwaiter()
                .GetResult();
        }

        return harness;
    }

    private static SafeDestructiveScreen CreateScreen(Workspace workspace) => new()
    {
        BackupTargetsOverride = new[]
        {
            new TuiSafeBackupTarget(workspace.TargetDirectory, "test-target", long.MaxValue / 4, long.MaxValue / 4),
        },
    };

    /// <summary>Throwaway source device file plus a temporary backup volume.</summary>
    private sealed class Workspace : IDisposable
    {
        private readonly string _root;

        public Workspace(int size = 4 * 1024 * 1024)
        {
            _root = Path.Combine(Path.GetTempPath(), "dcheck-safe", Guid.NewGuid().ToString("N"));
            SourceDirectory = Path.Combine(_root, "source");
            TargetDirectory = Path.Combine(_root, "target");
            Directory.CreateDirectory(SourceDirectory);
            Directory.CreateDirectory(TargetDirectory);

            SourcePath = Path.Combine(SourceDirectory, "disk.raw");
            Content = new byte[size];
            RandomNumberGenerator.Fill(Content);
            File.WriteAllBytes(SourcePath, Content);
        }

        public string Root => _root;

        public string SourceDirectory { get; }

        public string TargetDirectory { get; }

        public string SourcePath { get; }

        public byte[] Content { get; }

        public CoreDriveInfo CreateDisk() => new()
        {
            Path = SourcePath,
            Name = "SafeTest Disk 4MB",
            Model = "SafeTest Disk 4MB",
            SerialNumber = "SN-SAFE-0001",
            FirmwareVersion = "1.0",
            TotalSize = Content.Length,
            BusType = CoreBusType.Sata,
            Interface = "SATA",
            IsPhysical = true,
            IsReady = true,
            SupportsSmart = true,
            IsSystemDisk = false,
        };

        public string SingleImagePath() => Directory
            .EnumerateFiles(TargetDirectory, TuiSafeWorkflowService.ImageFileName, SearchOption.AllDirectories)
            .Single();

        public string SingleImageDirectory() => Path.GetDirectoryName(SingleImagePath())!;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
                // Temporary directories are best effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Temporary directories are best effort cleanup.
            }
        }
    }

    /// <summary>Source stream that reports chosen read calls as unreadable blocks.</summary>
    private sealed class FailingBlockStream : Stream
    {
        private readonly byte[] _content;
        private readonly HashSet<int> _failOnRead;
        private int _reads;
        private long _position;

        public FailingBlockStream(byte[] content, params int[] failOnRead)
        {
            _content = content;
            _failOnRead = new HashSet<int>(failOnRead);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _content.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            // Nothing is buffered.
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            int read = _reads++;
            if (_failOnRead.Contains(read))
            {
                // The device consumes the block even though it cannot deliver it: the
                // offset advances and the lost bytes have to be zero filled.
                _position = Math.Min(_content.Length, _position + count);
                throw new IOException("simulovaný nečitelný blok " + read.ToString(CultureInfo.InvariantCulture));
            }

            if (_position >= _content.Length)
            {
                return 0;
            }

            int available = (int)Math.Min(count, _content.Length - _position);
            Array.Copy(_content, _position, buffer, offset, available);
            _position += available;
            return available;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Sanitization fake that damages the disk and then fails, like a dying device.</summary>
    private sealed class CorruptingSanitizationService : IDiskSanitizationService
    {
        private readonly string _path;

        public CorruptingSanitizationService(string path) => _path = path;

        public int SanitizeCalls { get; private set; }

        public Task<SanitizationResult> SanitizeDiskAsync(
            string devicePath,
            long diskSize,
            bool createPartition = true,
            bool format = true,
            string volumeLabel = "Sanitized",
            IProgress<SanitizationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            SanitizeCalls++;
            progress?.Report(new SanitizationProgress
            {
                PhaseKind = SanitizationProgressPhase.Write,
                Phase = "Write zeros",
                ProgressPercent = 10,
                BytesProcessed = diskSize / 10,
                TotalBytes = diskSize,
                CurrentSpeedMBps = 150,
                Errors = 0,
                TotalElapsed = TimeSpan.FromSeconds(1),
            });

            // The destructive phase has already overwritten part of the disk.
            File.WriteAllBytes(_path, new byte[4096]);
            throw new IOException("simulované selhání destruktivní fáze");
        }

        public Task<SanitizationResult> CreatePartitionAsync(
            string devicePath,
            string volumeLabel = "Tested",
            bool format = true,
            IProgress<SanitizationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SanitizationResult { Success = true, PartitionCreated = true });
    }
}
