using System;
using Avalonia.Controls;
using DiskChecker.UI.Avalonia.ViewModels;

namespace DiskChecker.UI.Avalonia.Services.Interfaces;

/// <summary>
/// Service for handling navigation between views.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Gets the current view model.
    /// </summary>
    ViewModelBase? CurrentViewModel { get; }
    
    /// <summary>
    /// Navigates to the specified view model type.
    /// </summary>
    void NavigateTo<T>() where T : ViewModelBase;
    
    /// <summary>
    /// Registers a view for a view model type.
    /// </summary>
    void RegisterViewForViewModel<TViewModel, TView>()
        where TViewModel : ViewModelBase
        where TView : UserControl;
    
    /// <summary>
    /// Event raised when navigation occurs.
    /// </summary>
    event EventHandler<NavigationEventArgs>? Navigated;
}

/// <summary>
/// Event args for navigation events.
/// </summary>
public class NavigationEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the view model being navigated to.
    /// </summary>
    public ViewModelBase? ViewModel { get; set; }
}