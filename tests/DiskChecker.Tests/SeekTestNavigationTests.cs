using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.UI.Avalonia.ViewModels;
using DiskChecker.UI.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

[CollectionDefinition("Seek UI", DisableParallelization = true)]
public class SeekUiTestGroup : ICollectionFixture<SeekUiFixture>;

public sealed class SeekUiFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(SeekUiAppBuilder));
    public void Dispose() => Session.Dispose();
}

public static class SeekUiAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<DiskChecker.UI.Avalonia.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

[Collection("Seek UI")]
public class SeekTestNavigationTests(SeekUiFixture fixture)
{
    [Fact]
    public Task CompletedChartsCanBeRepeatedlyDetachedWithoutSharingSkiaResources()
        => fixture.Session.Dispatch(async () =>
        {
            using var provider = new ServiceCollection().AddTransient<SeekTestViewModel>(_ => CreateViewModel())
                .AddTransient<NavigationServiceTests.DisposablePage>().BuildServiceProvider();
            var navigation = new NavigationService(provider);
            var window = new Window { Width = 1100, Height = 900 };
            navigation.Navigated += (_, e) => window.Content = e.ViewModel is SeekTestViewModel vm
                ? new SeekTestView { DataContext = vm } : new TextBlock { Text = "Next view" };
            try
            {
                window.Show();
                for (var i = 0; i < 12; i++)
                {
                    navigation.NavigateTo<SeekTestViewModel>();
                    var vm = Assert.IsType<SeekTestViewModel>(navigation.CurrentViewModel);
                    await vm.StartTestCommand.ExecuteAsync(null);
                    Assert.True(vm.HasResults);
                    Assert.Equal(3, vm.FinalChartPointCount);
                    Assert.DoesNotContain(vm.LatencySeries, live => vm.FinalLatencySeries.Contains(live));
                    Assert.DoesNotContain(vm.LatencyXAxes, axis => vm.FinalLatencyXAxes.Contains(axis));
                    Assert.NotEmpty(vm.FinalLatencyXAxes);
                    Assert.NotEmpty(vm.FinalLatencyYAxes);
                    window.UpdateLayout();
                    await Task.Delay(60);
                    window.CaptureRenderedFrame()?.Dispose();
                    navigation.NavigateTo<NavigationServiceTests.DisposablePage>();
                    await Task.Delay(20);
                }
                navigation.NavigateTo<SeekTestViewModel>();
                var last = Assert.IsType<SeekTestViewModel>(navigation.CurrentViewModel);
                await last.StartTestCommand.ExecuteAsync(null);
                window.UpdateLayout();
                var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First();
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                await Task.Delay(100);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                if (Environment.GetEnvironmentVariable("DISKCHECKER_UI_SCREENSHOT") is { Length: > 0 } screenshot)
                {
                    using var output = File.Create(screenshot);
                    frame.Save(output);
                }
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);

    [Fact]
    public Task LeavingDuringTestIgnoresQueuedProgressAndLateResult()
        => fixture.Session.Dispatch(async () =>
        {
            var pending = new TaskCompletionSource<SeekTestResult>();
            var executor = CreateExecutor();
            Action<SeekTestProgress>? progress = null;
            CancellationToken token = default;
            executor.ExecuteAsync(Arg.Any<SeekTestRequest>(), Arg.Any<Action<SeekTestProgress>?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    progress = call.Arg<Action<SeekTestProgress>>();
                    token = call.Arg<CancellationToken>();
                    return pending.Task;
                });
            using var vm = CreateViewModel(executor);
            var run = vm.StartTestCommand.ExecuteAsync(null);
            Assert.True(vm.IsTesting);
            progress!(new SeekTestProgress { SeeksCompleted = 1, TotalSeeks = 3, PercentComplete = 33 });
            vm.Dispose();
            var changes = new List<string?>();
            vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
            Assert.True(token.IsCancellationRequested);
            using var registration = token.Register(() => { }); // CTS remains alive until executor exits.
            pending.SetResult(Result());
            await run;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(changes);
            Assert.False(vm.HasResults);
            Assert.Empty(vm.LatencyChartValues);
            return true;
        }, CancellationToken.None);

    [Fact]
    public Task LeavingDuringRecommendationIgnoresLateSmartResponse()
        => fixture.Session.Dispatch(async () =>
        {
            var pending = new TaskCompletionSource<SmartaData?>();
            var smart = Substitute.For<ISmartaProvider>();
            smart.GetSmartaDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
            using var vm = CreateViewModel(CreateExecutor(), smart);
            vm.SelectedDrive!.SupportsSmart = true;
            var load = vm.LoadRecommendationCommand.ExecuteAsync(null);
            vm.Dispose();
            var changes = new List<string?>();
            vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
            pending.SetResult(null);
            await load;
            Assert.Empty(changes);
            return true;
        }, CancellationToken.None);

    private static SeekTestViewModel CreateViewModel(ISeekTestExecutor? executor = null, ISmartaProvider? smart = null)
    {
        var selected = Substitute.For<ISelectedDiskService>();
        var drive = new CoreDriveInfo { Path = "/dev/test-fixture", Name = "Test disk", TotalSize = 1024 * 1024, SupportsSmart = false };
        selected.SelectedDisk.Returns(drive);
        return new SeekTestViewModel(Substitute.For<INavigationService>(), selected, Substitute.For<IDialogService>(),
            Substitute.For<ISettingsService>(), Substitute.For<IDiskCacheService>(),
            new SeekTestService(smart ?? Substitute.For<ISmartaProvider>(), executor ?? CreateExecutor(), NullLogger<SeekTestService>.Instance),
            Substitute.For<ICertificateGenerator>(), Substitute.For<IDiskCardRepository>(), null!)
        { SelectedDrive = drive, SelectedSeekCount = 3, MaxSafeSeekCount = 10000 };
    }

    private static ISeekTestExecutor CreateExecutor()
    {
        var executor = Substitute.For<ISeekTestExecutor>();
        executor.GetRecommendation(Arg.Any<SmartaData?>(), Arg.Any<long>(), Arg.Any<bool>())
            .Returns(new SeekTestRecommendation { RecommendedSeekCount = 3, MaxSafeSeekCount = 10000 });
        executor.ExecuteAsync(Arg.Any<SeekTestRequest>(), Arg.Any<Action<SeekTestProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result()));
        return executor;
    }

    private static SeekTestResult Result() => new()
    {
        // Incomplete avoids persistence: this test exercises charts/navigation only.
        IsCompleted = false, SeekCount = 3, AverageLatencyMs = 2, MinLatencyMs = 1, MaxLatencyMs = 3,
        Samples = new List<SeekLatencySample>
        {
            new() { Index = 1, LatencyMs = 1 }, new() { Index = 2, LatencyMs = 2 }, new() { Index = 3, LatencyMs = 3 }
        }
    };
}
