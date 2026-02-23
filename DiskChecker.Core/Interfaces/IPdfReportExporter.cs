using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Defines PDF export for report data.
/// </summary>
public interface IPdfReportExporter
{
    /// <summary>
    /// Generates an A4 certificate PDF document.
    /// </summary>
    /// <param name="report">Report data.</param>
    /// <returns>PDF bytes.</returns>
    byte[] GenerateCertificatePdf(TestReportData report);
}
