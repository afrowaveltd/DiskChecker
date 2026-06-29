namespace DiskChecker.UI.Avalonia.ViewModels;

public class LanguageItem
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Flag { get; init; } = string.Empty;
    
    public override string ToString() => Name;
}
