using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Sends test reports via email.
/// </summary>
public interface IReportEmailService
{
    /// <summary>
    /// Sends a report email to the provided recipient.
    /// </summary>
    /// <param name="report">Report data.</param>
    /// <param name="recipient">Recipient email address.</param>
    /// <param name="includeCertificate">Whether to include the certificate HTML.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SendReportAsync(
        TestReportData report,
        string recipient,
        bool includeCertificate,
        CancellationToken cancellationToken = default);
}
