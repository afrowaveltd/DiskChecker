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

    public event EventHandler<NavigationEventArgs>? Navigated;

    public void RegisterViewForViewModel<TViewModel, TView>()
        where TViewModel : ViewModelBase
        where TView : UserControl
    {
        // View resolution is done via ViewLocator
    }

    public void NavigateTo<T>() where T : ViewModelBase
    {
        // Dispose the current scope - this also disposes any IDisposable services
        // (including transient ViewModels) that were resolved from it. Do NOT call
        // disposable.Dispose() manually first to avoid double-disposal.
        _currentScope?.Dispose();

        var scope = _serviceProvider.CreateScope();
        var viewModel = scope.ServiceProvider.GetService<T>() ?? Activator.CreateInstance<T>();

        if (viewModel is null)
        {
            scope.Dispose();
            throw new InvalidOperationException($"Unable to create ViewModel instance for type {typeof(T).FullName}.");
        }

        _currentScope = scope;
        CurrentViewModel = viewModel;

        // Notify navigation
        if (viewModel is INavigableViewModel navigableViewModel)
        {
            navigableViewModel.OnNavigatedTo();
        }

        Navigated?.Invoke(this, new NavigationEventArgs { ViewModel = viewModel });
    }
}