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
        "10,90 70,85 130,80 190,75 250,70 310,65 370,60 430,58 490,55",
        "10,95 70,90 130,86 190,82 250,79 310,76 370,73 430,70 490,68",
        "10,110 490,110",
        false,
        "1 MB/s",
        "0.5 MB/s",
        "0 MB/s",
        "0 %",
        "50 %",
        "100 %");
}
