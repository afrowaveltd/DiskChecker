namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Indicates whether navigating away would dispose a ViewModel still owning a
/// running disk operation. Both the navigation service and main toolbar must
/// consult the same contract.
/// </summary>
public interface INavigationActivity
{
    /// <summary>True while leaving the current view would be unsafe.</summary>
    bool IsNavigationBlocked { get; }
}
