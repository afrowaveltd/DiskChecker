using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class AbsoluteDestructiveScreenTests
{
    private static TestHarness CreateHarness(
        FakeSmartaProvider provider,
        FakeSanitizationService sanitization,
        FakeSeekExecutor seekExecutor,
        out FakeDiskDetectionService detection)
    {
        var fakeDetection = new FakeDiskDetectionService();
        fakeDetection.Drives.Add(FakeDiskDetectionService.Sample());
        detection = fakeDetection;

        return TestHarness.Create(configure: services =>
        {
            services.RemoveAll<ISmartaProvider>();
            services.AddSingleton<ISmartaProvider>(provider);
            services.RemoveAll<IDiskDetectionService>();
            services.AddSingleton<IDiskDetectionService>(fakeDetection);
            services.RemoveAll<IDiskSanitizationService>();
            services.AddSingleton<IDiskSanitizationService>(sanitization);
            services.RemoveAll<ISeekTestExecutor>();
            services.AddSingleton<ISeekTestExecutor>(seekExecutor);
        });
    }

    private static AbsoluteDestructiveScreen CreateScreen() => new()
    {
        Cooldown = TimeSpan.Zero,
        FullStrokeSeeks = 5,
        RandomSeeks = 5,
        SkipSeeks = 5,
    };

    [Fact]
    public async Task FullWorkflowRunsAllPhasesAndPersistsCertificate()
    {
        var sanitization = new FakeSanitizationService();
        using var harness = CreateHarness(
            new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() },
            sanitization,
            new FakeSeekExecutor(),
            out var detection);

        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice("back");

        var screen = CreateScreen();
        var result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Equal(2, sanitization.SanitizeCalls);
        Assert.Equal(1, sanitization.PartitionCalls);
        Assert.NotNull(harness.Io.LastProgress);
        Assert.Equal(100, harness.Io.LastProgress!.Percent, 3);

        var progress = harness.Io.LastProgress!.Snapshot();
        Assert.Equal(8, progress.Phases.Count);
        Assert.All(progress.Phases, phase => Assert.NotEqual(TuiPhaseState.Pending, phase.State));
    }

    [Fact]
    public async Task WorkflowRequiresConfirmation()
    {
        var sanitization = new FakeSanitizationService();
        using var harness = CreateHarness(
            new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() },
            sanitization,
            new FakeSeekExecutor(),
            out var detection);

        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice("back");

        var screen = CreateScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(0, sanitization.SanitizeCalls);
        Assert.Contains("Potvrzení destruktivního testu", harness.Io.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SystemDiskIsRefused()
    {
        var sanitization = new FakeSanitizationService();
        using var harness = CreateHarness(
            new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() },
            sanitization,
            new FakeSeekExecutor(),
            out var detection);

        var drive = FakeDiskDetectionService.Sample();
        drive.IsSystemDisk = true;
        detection.Drives[0] = drive;
        harness.Context.Selection.SelectedDisk = drive;
        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueChoice("back");

        var screen = CreateScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(0, sanitization.SanitizeCalls);
    }

    [Fact]
    public async Task MissingDiskSelectionStopsImmediately()
    {
        var sanitization = new FakeSanitizationService();
        using var harness = CreateHarness(
            new FakeSmartaProvider(),
            sanitization,
            new FakeSeekExecutor(),
            out _);

        harness.Io.EnqueueChoice("back");

        var screen = CreateScreen();
        var result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Contains("vyberte disk", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SanitizationFailureAbortsTheWorkflowButKeepsTheUi()
    {
        var sanitization = new FakeSanitizationService { ThrowOnSanitize = true };
        using var harness = CreateHarness(
            new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() },
            sanitization,
            new FakeSeekExecutor(),
            out var detection);

        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("start");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice("back");

        var screen = CreateScreen();
        var result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Contains("přerušen", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
    }
}
