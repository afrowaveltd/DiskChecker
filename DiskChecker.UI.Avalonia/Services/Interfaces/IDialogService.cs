using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.Services.Interfaces;

public interface IDialogService
{
    /// <summary>
    /// Show information message dialog
    /// </summary>
    Task ShowInfoAsync(string title, string message);
    
    /// <summary>
    /// Show generic message dialog (same as Info)
    /// </summary>
    Task ShowMessageAsync(string title, string message);
    
    /// <summary>
    /// Show success message dialog with green icon
    /// </summary>
    Task ShowSuccessAsync(string title, string message);
    
    /// <summary>
    /// Show warning message dialog with yellow icon
    /// </summary>
    Task ShowWarningAsync(string title, string message);
    
    /// <summary>
    /// Show error message dialog with red icon
    /// </summary>
    Task ShowErrorAsync(string title, string message);
    
    /// <summary>
    /// Show alert dialog (same as Warning)
    /// </summary>
    Task ShowAlertAsync(string title, string message);
    
    /// <summary>
    /// Show confirmation dialog with Yes/No or Yes/No/Cancel buttons
    /// </summary>
    Task<bool> ShowConfirmationAsync(string title, string message);
    
    /// <summary>
    /// Show dangerous confirmation dialog for destructive operations
    /// </summary>
    Task<bool> ShowDangerConfirmationAsync(string title, string message);

    /// <summary>
    /// Show input dialog with text box
    /// </summary>
    Task<string?> ShowPromptAsync(string title, string message, string defaultValue = "");
    
    /// <summary>
    /// Show input dialog with text box (same as PromptAsync)
    /// </summary>
    Task<string?> ShowInputDialogAsync(string title, string message, string defaultValue = "");
}