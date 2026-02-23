namespace DiskChecker.Core.Models;

/// <summary>
/// Aggregates SMART and surface test results for reporting.
/// </summary>
public class TestReportData
{
    /// <summary>
    /// Gets or sets the SMART check result.
    /// </summary>
    public SmartCheckResult SmartCheck { get; set; } = new();

    /// <summary>
    /// Gets or sets the surface test result, if available.
    /// </summary>
    public SurfaceTestResult? SurfaceTest { get; set; }

    /// <summary>
    /// Gets or sets the report language tag.
    /// </summary>
    public string Language { get; set; } = "cs-CZ";
}
