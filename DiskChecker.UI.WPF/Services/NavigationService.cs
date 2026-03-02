using System;
using System.Collections.Generic;
using DiskChecker.Core.Models;
using DiskChecker.UI.WPF.ViewModels;

namespace DiskChecker.UI.WPF.Services;

/// <summary>
/// Služba pro navigaci mezi Views.
/// Používá View/ViewModel mapping pro MVVM navigation.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Registruje mapping mezi ViewModel a View typem.
    /// </summary>
    void RegisterViewForViewModel<TViewModel, TView>()
        where TViewModel : class
        where TView : class;

    /// <summary>
    /// Naviguje na View odpovídající danému ViewModel typu.
    /// </summary>
    void NavigateTo<TViewModel>(object? parameter = null) where TViewModel : class;

    /// <summary>
    /// Vrací na předchozí View.
    /// </summary>
    void GoBack();

    /// <summary>
    /// Aktuální ViewModel.
    /// </summary>
    object? CurrentViewModel { get; }

    /// <summary>
    /// Aktuální View.
    /// </summary>
    object? CurrentView { get; }

    /// <summary>
    /// Event když se změní View.
    /// </summary>
    event EventHandler<ViewChangedEventArgs>? ViewChanged;
}

/// <summary>
/// Arguments pro ViewChanged event.
/// </summary>
public class ViewChangedEventArgs : EventArgs
{
    public required Type ViewModelType { get; set; }
    public required object View { get; set; }
    public required object ViewModel { get; set; }
}

/// <summary>
/// Implementace NavigationService.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, Type> _viewModelViewMapping;
    private readonly Stack<(object ViewModel, object View)> _navigationStack;

    private object? _currentViewModel;
    private object? _currentView;

    public object? CurrentViewModel => _currentViewModel;
    public object? CurrentView => _currentView;

    public event EventHandler<ViewChangedEventArgs>? ViewChanged;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _viewModelViewMapping = new Dictionary<Type, Type>();
        _navigationStack = new Stack<(object, object)>();
    }

    /// <summary>
    /// Registruje mapping mezi ViewModel a View typem.
    /// </summary>
    public void RegisterViewForViewModel<TViewModel, TView>()
        where TViewModel : class
        where TView : class
    {
        _viewModelViewMapping[typeof(TViewModel)] = typeof(TView);
    }

    public void NavigateTo<TViewModel>(object? parameter = null) where TViewModel : class
    {
        var vmType = typeof(TViewModel);
        
        if (!_viewModelViewMapping.TryGetValue(vmType, out var viewType))
        {
            throw new InvalidOperationException($"Žádné View registrováno pro ViewModel {vmType.Name}");
        }

        // Vytvořit instance
        var viewModel = _serviceProvider.GetService(vmType)
            ?? throw new InvalidOperationException($"Nelze vytvořit ViewModel {vmType.Name}");
        
        var view = Activator.CreateInstance(viewType)
            ?? throw new InvalidOperationException($"Nelze vytvořit View {viewType.Name}");

        // Nastavit DataContext
        if (view is System.Windows.Controls.UserControl userControl)
        {
            userControl.DataContext = viewModel;
        }
        else if (view is System.Windows.Window window)
        {
            window.DataContext = viewModel;
        }

        ApplyNavigationParameter(viewModel, parameter);

        // Inicializovat ViewModel asynchronně, aby nedošlo k blokování UI threadu.
        if (viewModel is ViewModels.ViewModelBase vmBase)
        {
            _ = InitializeViewModelAsync(vmBase);
        }

        // Uložit předchozí stav
        if (_currentViewModel != null && _currentView != null)
        {
            _navigationStack.Push((_currentViewModel, _currentView));
        }

        // Nastavit nový stav
        _currentViewModel = viewModel;
        _currentView = view;

        // Vyvolat event
        ViewChanged?.Invoke(this, new ViewChangedEventArgs
        {
            ViewModelType = vmType,
            View = view,
            ViewModel = viewModel
        });
    }

    private static async Task InitializeViewModelAsync(ViewModels.ViewModelBase vmBase)
    {
        await vmBase.InitializeAsync();
    }

    private static void ApplyNavigationParameter(object viewModel, object? parameter)
    {
        if (parameter is not CoreDriveInfo drive)
        {
            return;
        }

        switch (viewModel)
        {
            case SurfaceTestViewModel surfaceTestViewModel:
                surfaceTestViewModel.SetSelectedDrive(drive);
                break;
            case SmartCheckViewModel smartCheckViewModel:
                smartCheckViewModel.SetSelectedDrive(drive);
                break;
        }
    }

    public void GoBack()
    {
        if (_navigationStack.Count == 0)
            return;

        var (previousVM, previousView) = _navigationStack.Pop();
        _currentViewModel = previousVM;
        _currentView = previousView;

        ViewChanged?.Invoke(this, new ViewChangedEventArgs
        {
            ViewModelType = previousVM.GetType(),
            View = previousView,
            ViewModel = previousVM
        });
    }
}
