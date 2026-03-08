namespace DiskChecker.UI.Avalonia.Services.Interfaces;

/// <summary>
/// Service for displaying dialogs to the user.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Show a message dialog.
    /// </summary>
    Task ShowMessageAsync(string title, string message);
    
    /// <summary>
    /// Show an error dialog.
    /// </summary>
    Task ShowErrorAsync(string title, string message);
    
    /// <summary>
    /// Show a confirmation dialog and return true if user clicked OK.
    /// </summary>
    Task<bool> ShowConfirmationAsync(string title, string message);
}
