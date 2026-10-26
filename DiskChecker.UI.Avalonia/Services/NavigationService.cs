using System;
using Avalonia.Controls;
using DiskChecker.UI.Avalonia.ViewModels;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Implementation of navigation service for managing view navigation.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private IServiceScope? _currentScope;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ViewModelBase? CurrentViewModel { get; private set; }

    public event EventHandler<AppNavigationEventArgs>? Navigated;

    public void RegisterViewForViewModel<TViewModel, TView>()
        where TViewModel : ViewModelBase
        where TView : UserControl
    {
        // View resolution is done via ViewLocator
    }

    public void NavigateTo<T>() where T : ViewModelBase
    {
        var scope = _serviceProvider.CreateScope();
        T viewModel;
        try
        {
            viewModel = scope.ServiceProvider.GetService<T>() ?? Activator.CreateInstance<T>();
            if (viewModel is INavigableViewModel navigableViewModel)
                navigableViewModel.OnNavigatedTo();
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        var previousScope = _currentScope;
        _currentScope = scope;
        CurrentViewModel = viewModel;
        try
        {
            // Replace/detach the old view before disposing its bound services.
            Navigated?.Invoke(this, new AppNavigationEventArgs { ViewModel = viewModel });
        }
        finally
        {
            // DI owns transient IDisposable view models: dispose exactly once.
            previousScope?.Dispose();
        }
    }
}
