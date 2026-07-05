namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Represents a test profile item for UI display.
/// </summary>
public class TestProfileItem : ObservableObject
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private bool _isSelected;
    private bool _isDestructive;

    /// <summary>
    /// Stable key for identifying this profile regardless of locale.
    /// Used in switch statements instead of localized Name.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// Whether this test performs destructive operations (disk wipe).
    /// </summary>
    public bool IsDestructive
    {
        get => _isDestructive;
        set => SetProperty(ref _isDestructive, value);
    }
}