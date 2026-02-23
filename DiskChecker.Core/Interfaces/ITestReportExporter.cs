using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Defines exporting of test results into different formats.
/// </summary>
public interface ITestReportExporter
{
    /// <summary>
    /// Generates a plain text report.
    /// </summary>
    /// <param name="report">Report data.</param>
    /// <returns>Text report.</returns>
    string GenerateText(TestReportData report);

    /// <summary>
    /// Generates a full HTML report.
    /// </summary>
    /// <param name="report">Report data.</param>
    /// <returns>HTML report.</returns>
    string GenerateHtml(TestReportData report);

    /// <summary>
    /// Generates a CSV report.
    /// </summary>
    /// <param name="report">Report data.</param>
    /// <returns>CSV report.</returns>
    string GenerateCsv(TestReportData report);

    /// <summary>
    /// Generates an A4 certificate HTML layout.
    /// </summary>
    /// <param name="report">Report data.</param>
    /// <returns>Certificate HTML.</returns>
    string GenerateCertificateHtml(TestReportData report);
}
