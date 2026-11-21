using System;
using DiskChecker.UI.Avalonia.Services;
using DiskChecker.UI.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiskChecker.Tests;

public class NavigationServiceTests
{
    [Fact]
    public void OldScopeIsDisposedOnceAfterViewReplacement()
    {
        using var provider = new ServiceCollection().AddTransient<DisposablePage>().BuildServiceProvider();
        var navigation = new NavigationService(provider);
        navigation.NavigateTo<DisposablePage>();
        var previous = Assert.IsType<DisposablePage>(navigation.CurrentViewModel);
        navigation.Navigated += (_, _) => Assert.Equal(0, previous.DisposeCount);

        navigation.NavigateTo<DisposablePage>();

        Assert.Equal(1, previous.DisposeCount);
        Assert.NotSame(previous, navigation.CurrentViewModel);
    }

    [Fact]
    public void FailedDestinationLeavesCurrentPageUsable()
    {
        using var provider = new ServiceCollection().AddTransient<DisposablePage>()
            .AddTransient<BrokenPage>().BuildServiceProvider();
        var navigation = new NavigationService(provider);
        navigation.NavigateTo<DisposablePage>();
        var previous = Assert.IsType<DisposablePage>(navigation.CurrentViewModel);

        Assert.Throws<InvalidOperationException>(() => navigation.NavigateTo<BrokenPage>());

        Assert.Same(previous, navigation.CurrentViewModel);
        Assert.Equal(0, previous.DisposeCount);
    }

    [Fact]
    public void BusyPageBlocksEveryNavigationPathUntilOperationCompletes()
    {
        using var provider = new ServiceCollection()
            .AddTransient<BusyPage>()
            .AddTransient<DisposablePage>()
            .AddTransient<BrokenPage>()
            .BuildServiceProvider();
        var navigation = new NavigationService(provider);
        navigation.NavigateTo<BusyPage>();
        var busy = Assert.IsType<BusyPage>(navigation.CurrentViewModel);
        var notifications = 0;
        navigation.Navigated += (_, _) => notifications++;

        // No new scope is constructed and the active VM cannot be disposed.
        navigation.NavigateTo<DisposablePage>();
        navigation.NavigateTo<BrokenPage>();
        Assert.Same(busy, navigation.CurrentViewModel);
        Assert.Equal(0, busy.DisposeCount);
        Assert.Equal(0, notifications);

        busy.IsNavigationBlocked = false;
        navigation.NavigateTo<DisposablePage>();
        Assert.IsType<DisposablePage>(navigation.CurrentViewModel);
        Assert.Equal(1, busy.DisposeCount);
        Assert.Equal(1, notifications);
    }

    public sealed class BusyPage : ViewModelBase, INavigationActivity, IDisposable
    {
        public bool IsNavigationBlocked { get; set; } = true;
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    public sealed class DisposablePage : ViewModelBase, IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    public sealed class BrokenPage : ViewModelBase, INavigableViewModel
    {
        public void OnNavigatedTo() => throw new InvalidOperationException("Initialization failed");
    }
}
