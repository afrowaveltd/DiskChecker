using Avalonia.Controls;

namespace DiskChecker.UI.Avalonia.Services.Interfaces;

/// <summary>
/// Service for navigating between views in the application.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Navigate to a view by type.
    /// </summary>
    void NavigateTo<T>() where T : UserControl;
    
    /// <summary>
    /// Navigate to a view by type with parameters.
    /// </summary>
    void NavigateTo<T>(object parameter) where T : UserControl;
    
    /// <summary>
    /// Go back to previous view.
    /// </summary>
    void GoBack();
    
    /// <summary>
    /// Get the current view type.
    /// </summary>
    Type? CurrentView { get; }
}
