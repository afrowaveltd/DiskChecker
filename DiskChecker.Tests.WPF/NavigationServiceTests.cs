using Xunit;
using NSubstitute;
using DiskChecker.UI.WPF.Services;
using DiskChecker.UI.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DiskChecker.Tests.WPF.Services;

/// <summary>
/// Unit testy pro NavigationService.
/// </summary>
public class NavigationServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly NavigationService _navigationService;

    public NavigationServiceTests()
    {
        // Arrange - vytvoření mock service provideru
        var services = new ServiceCollection();
        services.AddTransient<DiskSelectionViewModel>();
        services.AddTransient<SmartCheckViewModel>();
        services.AddTransient<SurfaceTestViewModel>();
        
        // Mock dependencies
        services.AddSingleton(Substitute.For<DiskChecker.Application.Services.DiskCheckerService>());
        services.AddSingleton(Substitute.For<DiskChecker.Application.Services.SurfaceTestService>());
        
        _serviceProvider = services.BuildServiceProvider();
        _navigationService = new NavigationService(_serviceProvider);
        
        // Register navigation service AFTER creating it
        services = new ServiceCollection();
        services.AddTransient<DiskSelectionViewModel>();
        services.AddTransient<SmartCheckViewModel>();
        services.AddTransient<SurfaceTestViewModel>();
        services.AddSingleton(Substitute.For<DiskChecker.Application.Services.DiskCheckerService>());
        services.AddSingleton(Substitute.For<DiskChecker.Application.Services.SurfaceTestService>());
        services.AddSingleton<INavigationService>(_navigationService);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Test registrace ViewForViewModel.
    /// </summary>
    [Fact]
    public void RegisterViewForViewModel_ShouldAllowNavigation()
    {
        // Arrange
        var navService = new NavigationService(_serviceProvider);
        navService.RegisterViewForViewModel<DiskSelectionViewModel, TestView>();

        // Act
        navService.NavigateTo<DiskSelectionViewModel>();

        // Assert
        Assert.NotNull(navService.CurrentViewModel);
        Assert.IsType<DiskSelectionViewModel>(navService.CurrentViewModel);
        Assert.IsType<TestView>(navService.CurrentView);
    }

    /// <summary>
    /// Test navigace bez registrace View.
    /// </summary>
    [Fact]
    public void NavigateTo_ShouldThrowException_WhenViewNotRegistered()
    {
        // Arrange
        var navService = new NavigationService(_serviceProvider);
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => 
            navService.NavigateTo<DiskSelectionViewModel>());
    }

    /// <summary>
    /// Test ViewChanged event.
    /// </summary>
    [Fact]
    public void NavigateTo_ShouldRaiseViewChangedEvent()
    {
        // Arrange
        var navService = new NavigationService(_serviceProvider);
        navService.RegisterViewForViewModel<DiskSelectionViewModel, TestView>();
        ViewChangedEventArgs? capturedArgs = null;
        navService.ViewChanged += (sender, args) => capturedArgs = args;

        // Act
        navService.NavigateTo<DiskSelectionViewModel>();

        // Assert
        Assert.NotNull(capturedArgs);
        Assert.Equal(typeof(DiskSelectionViewModel), capturedArgs.ViewModelType);
        Assert.IsType<DiskSelectionViewModel>(capturedArgs.ViewModel);
        Assert.IsType<TestView>(capturedArgs.View);
    }

    /// <summary>
    /// Test GoBack functionality.
    /// </summary>
    [Fact]
    public void GoBack_ShouldNavigateToPreviousView()
    {
        // Arrange
        var navService = new NavigationService(_serviceProvider);
        navService.RegisterViewForViewModel<DiskSelectionViewModel, TestView>();
        navService.RegisterViewForViewModel<SmartCheckViewModel, TestView>();
        
        navService.NavigateTo<DiskSelectionViewModel>();
        var firstViewModel = navService.CurrentViewModel;
        
        navService.NavigateTo<SmartCheckViewModel>();

        // Act
        navService.GoBack();

        // Assert
        Assert.Equal(firstViewModel, navService.CurrentViewModel);
        Assert.IsType<DiskSelectionViewModel>(navService.CurrentViewModel);
    }

    /// <summary>
    /// Test GoBack když není žádná historie.
    /// </summary>
    [Fact]
    public void GoBack_ShouldDoNothing_WhenNoHistory()
    {
        // Arrange
        var navService = new NavigationService(_serviceProvider);
        navService.RegisterViewForViewModel<DiskSelectionViewModel, TestView>();
        navService.NavigateTo<DiskSelectionViewModel>();
        var currentViewModel = navService.CurrentViewModel;

        // Act
        navService.GoBack();

        // Assert
        Assert.Equal(currentViewModel, navService.CurrentViewModel);
    }

    /// <summary>
    /// Test navigation stack.
    /// </summary>
    [Fact]
    public void NavigationStack_ShouldWorkCorrectly()
    {
        // Arrange
        var navService = new NavigationService(_serviceProvider);
        navService.RegisterViewForViewModel<DiskSelectionViewModel, TestView>();
        navService.RegisterViewForViewModel<SmartCheckViewModel, TestView>();
        navService.RegisterViewForViewModel<SurfaceTestViewModel, TestView>();

        // Act - navigace vpřed
        navService.NavigateTo<DiskSelectionViewModel>();
        var vm1 = navService.CurrentViewModel;
        
        navService.NavigateTo<SmartCheckViewModel>();
        var vm2 = navService.CurrentViewModel;
        
        navService.NavigateTo<SurfaceTestViewModel>();
        var vm3 = navService.CurrentViewModel;

        // Assert - navigace zpět
        Assert.IsType<SurfaceTestViewModel>(vm3);
        
        navService.GoBack();
        Assert.Equal(vm2, navService.CurrentViewModel);
        
        navService.GoBack();
        Assert.Equal(vm1, navService.CurrentViewModel);
        
        navService.GoBack(); // Žádná změna
        Assert.Equal(vm1, navService.CurrentViewModel);
    }

    /// <summary>
    /// Testovací View pro účely testování.
    /// </summary>
    private sealed class TestView { }
}
