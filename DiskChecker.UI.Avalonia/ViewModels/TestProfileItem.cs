using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Represents a test profile item for selection in the UI.
/// </summary>
public class TestProfileItem : ObservableObject
{
    private bool _isSelected;

    /// <summary>
    /// Gets or sets the name of the test profile.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the test profile.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this profile is currently selected.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}