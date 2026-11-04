using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Screens;
using DiskChecker.TUI.Services;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class SmartCheckScreenTests
{
    private static TestHarness CreateHarness(FakeSmartaProvider provider, out FakeDiskDetectionService detection)
    {
        var fakeDetection = new FakeDiskDetectionService();
        fakeDetection.Drives.Add(FakeDiskDetectionService.Sample());
        detection = fakeDetection;

        return TestHarness.Create(configure: services =>
        {
            services.RemoveAll<ISmartaProvider>();
            services.AddSingleton<ISmartaProvider>(provider);
            services.AddSingleton<IAdvancedSmartaProvider>(provider);
            services.RemoveAll<IDiskDetectionService>();
            services.AddSingleton<IDiskDetectionService>(fakeDetection);
        });
    }

    [Fact]
    public async Task RendersDiskIdentityAndHealthMetrics()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        using var harness = CreateHarness(provider, out var detection);
        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("back");

        var screen = new SmartCheckScreen();
        var result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(harness.Io.Contains("TestDisk 500GB"));
        Assert.True(harness.Io.Contains("SN-TEST-0001"));
        Assert.True(harness.Io.Contains("34°C"));
        Assert.True(harness.Io.Contains("Provozní hodiny"));
    }

    [Fact]
    public async Task ReportsUnavailableSmartData()
    {
        var provider = new FakeSmartaProvider { Data = null };
        using var harness = CreateHarness(provider, out var detection);
        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("back");

        var screen = new SmartCheckScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("SMART"));
    }

    [Fact]
    public async Task SelfTestStartsThroughProvider()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        using var harness = CreateHarness(provider, out var detection);
        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("short");
        harness.Io.EnqueueConfirm(true);
        harness.Io.EnqueueChoice("back");

        var screen = new SmartCheckScreen();
        var result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.True(provider.SelfTestStarted);
        Assert.Equal(SmartaSelfTestType.ShortTest, provider.LastStartedType);
    }

    [Fact]
    public async Task SelfTestIsRefusedOnSystemDisk()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        using var harness = CreateHarness(provider, out var detection);
        var drive = FakeDiskDetectionService.Sample();
        drive.IsSystemDisk = true;
        detection.Drives[0] = drive;
        harness.Context.Selection.SelectedDisk = drive;
        harness.Io.EnqueueChoice("short");
        harness.Io.EnqueueChoice("back");

        var screen = new SmartCheckScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.False(provider.SelfTestStarted);
    }

    [Fact]
    public async Task SelfTestConfirmationCanBeDeclined()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        using var harness = CreateHarness(provider, out var detection);
        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("extended");
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice("back");

        var screen = new SmartCheckScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.False(provider.SelfTestStarted);
    }

    [Fact]
    public async Task AttributesActionRendersAttributeTable()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        using var harness = CreateHarness(provider, out var detection);
        harness.Context.Selection.SelectedDisk = detection.Drives[0];
        harness.Io.EnqueueChoice("attributes");
        harness.Io.EnqueueChoice("back");

        var screen = new SmartCheckScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.True(harness.Io.Contains("Reallocated_Sector_Ct"));
        Assert.True(harness.Io.Contains("Temperature_Celsius"));
    }

    [Fact]
    public void CertificateNotesSummariseSmartState()
    {
        var smart = FakeSmartaProvider.Sample();
        smart.ReallocatedSectorCount = 3;
        smart.PendingSectorCount = 1;
        string notes = SmartCheckScreen.BuildSmartCertificateNotes(smart);

        Assert.Contains("SMART-only certifikát", notes, StringComparison.Ordinal);
        Assert.Contains("Realokované sektory: 3", notes, StringComparison.Ordinal);
        Assert.Contains("Čekající sektory: 1", notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingDiskSelectionPromptsDiskPicker()
    {
        var provider = new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() };
        using var harness = CreateHarness(provider, out var detection);
        harness.Io.EnqueueChoice(detection.Drives[0].Path);
        harness.Io.EnqueueChoice("back");

        var screen = new SmartCheckScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.NotNull(harness.Context.Selection.SelectedDisk);
        Assert.Equal(detection.Drives[0].Path, harness.Context.Selection.SelectedDisk!.Path);
    }
}
