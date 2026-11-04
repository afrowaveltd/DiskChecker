using DiskChecker.Core.Interfaces;
using DiskChecker.TUI.Screens;
using DiskChecker.Tui.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DiskChecker.Tui.Tests;

public sealed class SeekTestScreenTests
{
    private static TestHarness CreateHarness(FakeSeekExecutor executor, FakeSmartaProvider provider)
    {
        var detection = new FakeDiskDetectionService();
        detection.Drives.Add(FakeDiskDetectionService.Sample());
        return TestHarness.Create(configure: services =>
        {
            services.RemoveAll<ISmartaProvider>();
            services.AddSingleton<ISmartaProvider>(provider);
            services.RemoveAll<IDiskDetectionService>();
            services.AddSingleton<IDiskDetectionService>(detection);
            services.RemoveAll<ISeekTestExecutor>();
            services.AddSingleton<ISeekTestExecutor>(executor);
        });
    }

    [Fact]
    public async Task RendersRecommendationForSelectedDisk()
    {
        using var harness = CreateHarness(new FakeSeekExecutor(), new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() });
        harness.Context.Selection.SelectedDisk = FakeDiskDetectionService.Sample();
        harness.Io.EnqueueChoice("back");

        var screen = new SeekTestScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Contains("Doporučení", harness.Io.Output, StringComparison.Ordinal);
        Assert.Contains("1000", harness.Io.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecommendedSeekTestRunsAndShowsLatency()
    {
        using var harness = CreateHarness(new FakeSeekExecutor(), new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() });
        harness.Context.Selection.SelectedDisk = FakeDiskDetectionService.Sample();
        harness.Io.EnqueueChoice("recommended");
        harness.Io.EnqueueChoice("back");

        var screen = new SeekTestScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Contains("Test dokončen", harness.Io.Output, StringComparison.Ordinal);
        Assert.Contains("ms", harness.Io.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FragileDiskRequiresExplicitConfirmation()
    {
        var executor = new FakeSeekExecutor { TooFragile = true };
        using var harness = CreateHarness(executor, new FakeSmartaProvider { Data = FakeSmartaProvider.Sample() });
        harness.Context.Selection.SelectedDisk = FakeDiskDetectionService.Sample();
        harness.Io.EnqueueChoice("random");
        harness.Io.EnqueueConfirm(false);
        harness.Io.EnqueueChoice("back");

        var screen = new SeekTestScreen();
        await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Contains("příliš opotřebený", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingDiskShowsWarning()
    {
        using var harness = CreateHarness(new FakeSeekExecutor(), new FakeSmartaProvider());
        harness.Io.EnqueueChoice("back");

        var screen = new SeekTestScreen();
        var result = await screen.ShowAsync(harness.Context, TestContext.Current.CancellationToken);

        Assert.Equal(ScreenAction.Back, result.Action);
        Assert.Contains("vyberte disk", harness.Io.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(10, "A")]
    [InlineData(18, "B")]
    [InlineData(23, "C")]
    [InlineData(28, "D")]
    [InlineData(45, "E")]
    public void FullStrokeGradeUsesLatencyThresholds(double latency, string expected)
    {
        var result = new DiskChecker.Core.Models.SeekTestResult
        {
            TestType = DiskChecker.Core.Models.SeekTestType.FullStroke,
            IsCompleted = true,
            Samples =
            {
                new DiskChecker.Core.Models.SeekLatencySample { Index = 1, LatencyMs = latency },
                new DiskChecker.Core.Models.SeekLatencySample { Index = 2, LatencyMs = latency },
            },
        };

        Assert.Equal(expected, SeekGrade.Calculate(result).Grade);
    }

    [Fact]
    public void IncompleteSeekResultIsGradedF()
    {
        var result = new DiskChecker.Core.Models.SeekTestResult { IsCompleted = false };
        Assert.Equal("F", SeekGrade.Calculate(result).Grade);
    }
}
