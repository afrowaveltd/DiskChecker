namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// Shared graph data structure used by certificate views for chart rendering.
/// </summary>
public readonly record struct CertificateGraphData(
    string WriteProfilePoints,
    string ReadProfilePoints,
    string TemperatureProfilePoints,
    bool HasTemperatureProfile,
    string ChartMaxSpeedLabel,
    string ChartMidSpeedLabel,
    string ChartMinSpeedLabel,
    string ChartXAxisStartLabel,
    string ChartXAxisMidLabel,
    string ChartXAxisEndLabel)
{
    /// <summary>Never display demo curves as though they were measurements.</summary>
    public static CertificateGraphData Default { get; } = new(
        "",
        "",
        "",
        false,
        "N/A",
        "N/A",
        "0 MB/s",
        "0 %",
        "50 %",
        "100 %");
}
