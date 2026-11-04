using DiskChecker.Core.Interfaces;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class SurfaceTestScreenTests
{
    private static TestHarness CreateHarness(FakeSmartaProvider provider, FakeSurfaceRunner runner)
    {
        var detection = new FakeDiskDetectionService();
        detection.Drives.Add(FakeDiskDetectionService.Sample());
        return TestHarness.Create(configure: services =>
        {
            services.RemoveAll<ISmartaProvider>();
            services.AddSingleton<ISmartaProvider>(provider);
            services.RemoveAll<IDiskDetectionService>();
            services.AddSingleton<IDiskDetectionService>(detection);
            services.RemoveAll<ITuiSurfaceTestRunner>();
            services.AddSingleton<ITuiSurfaceTestRunner>(runner);
        });
    }

    [Fact]
    public async Task ReadTestRunsAndPersistsSession()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        using var harness = CreateHarness(provider, new FakeSurfaceRunner());
        harness.Context.Selection.SelectedDisk = FakeDiskDetectionService.Sample();
        harness.Io.EnqueueChoice("read");
        harness.Io.EnqueueChoice("back");

        var screen = new SurfaceTestScreen();
        var result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Contains("Test dokončen", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(harness.Io.LastProgress);
        Assert.Equal(100, harness.Io.LastProgress!.Percent, 3);
    }

    [Fact]
    public async Task WriteTestRequiresConfirmation()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        var runner = new FakeSurfaceRunner();
        using var harness = CreateHarness(provider, runner);
        harness.Context.Selection.SelectedDisk = FakeDiskDetectionService.Sample();
        harness.Io.EnqueueChoice("write");
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice("back");

        var screen = new SurfaceTestScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Contains("Destruktivní test povrchu", harness.Io.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SanitizationRunsThroughSanitizationService()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        var sanitization = new FakeSanitizationService();
        var detection = new FakeDiskDetectionService();
        detection.Drives.Add(FakeDiskDetectionService.Sample());

        using var harness = TestHarness.Create(configure: services =>
        {
            services.RemoveAll<ISmartaProvider>();
            services.AddSingleton<ISmartaProvider>(provider);
            services.RemoveAll<IDiskDetectionService>();
            services.AddSingleton<IDiskDetectionService>(detection);
            services.RemoveAll<ISurfaceTestService>();
            services.RemoveAll<IDiskSanitizationService>();
            services.AddSingleton<IDiskSanitizationService>(sanitization);
        });

        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("sanitize");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice("back");

        var screen = new SurfaceTestScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(1, sanitization.SanitizeCalls);
        Assert.Contains("Sanitizace", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiskPickerIsOfferedWhenNoDiskSelected()
    {
        var provider = new FakeSmartaProvider { Data = null };
        using var harness = CreateHarness(provider, new FakeSurfaceRunner());
        harness.Io.EnqueueChoice("/dev/sdz");
        harness.Io.EnqueueChoice("back");

        var screen = new SurfaceTestScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.NotNull(harness.Context.Selection.SelectedDisk);
    }
}
