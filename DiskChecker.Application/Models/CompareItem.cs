namespace DiskChecker.Application.Models;

/// <summary>
/// Single item for display in comparison reports.
/// </summary>
public class CompareItem
{
    public string Label { get; set; } = string.Empty;
    public string Value1 { get; set; } = string.Empty;
    public string Value2 { get; set; } = string.Empty;
}