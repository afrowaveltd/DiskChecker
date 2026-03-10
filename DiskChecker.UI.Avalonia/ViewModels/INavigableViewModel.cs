namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Interface for view models that can be navigated to.
/// Implementations should perform initialization in OnNavigatedTo.
/// </summary>
public interface INavigableViewModel
{
    /// <summary>
    /// Called when the view model is navigated to.
    /// Use this method to load data or initialize state.
    /// </summary>
    void OnNavigatedTo();
}