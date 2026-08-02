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
    public static CertificateGraphData Default { get; } = new(
        "56,176 160,166 264,156 368,145 471,135 575,125 679,115 783,111 886,105",
        "56,187 160,176 264,168 368,160 471,154 575,148 679,141 783,135 886,131",
        "56,226 886,226",
        false,
        "1 MB/s",
        "0.5 MB/s",
        "0 MB/s",
        "0 %",
        "50 %",
        "100 %");
}
