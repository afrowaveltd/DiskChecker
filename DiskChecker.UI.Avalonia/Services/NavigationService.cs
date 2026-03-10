using System;
using System.Collections.Generic;
using Avalonia.Controls;
using DiskChecker.UI.Avalonia.ViewModels;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Implementation of navigation service for managing view navigation.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly Dictionary<Type, Type> _viewModelToViewMap = new();
    private readonly Dictionary<Type, object> _viewModels = new();
    private readonly IServiceProvider _serviceProvider;

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
        _viewModelToViewMap[typeof(TViewModel)] = typeof(TView);
    }

    public void NavigateTo<T>() where T : ViewModelBase
    {
        var viewModelType = typeof(T);
        
        // Singleton instance management
        if (!_viewModels.TryGetValue(viewModelType, out var instance))
        {
            instance = _serviceProvider.GetService(viewModelType) ?? Activator.CreateInstance(viewModelType);
            _viewModels[viewModelType] = instance!;
        }

        var viewModel = (T)instance!;
        
        // Notify current view model that it's being navigated away from
        if (CurrentViewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }

        CurrentViewModel = viewModel;
        
        // Notify new view model that it's being navigated to
        if (viewModel is INavigableViewModel navigableViewModel)
        {
            navigableViewModel.OnNavigatedTo();
        }

        Navigated?.Invoke(this, new NavigationEventArgs { ViewModel = viewModel });
    }
}