using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services;

/// <summary>
/// Composes and sends test reports via SMTP.
/// </summary>
public class ReportEmailService : IReportEmailService
{
    private readonly ITestReportExporter _exporter;
    private readonly IEmailSender _emailSender;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportEmailService"/> class.
    /// </summary>
    /// <param name="exporter">Report exporter.</param>
    /// <param name="emailSender">Email sender.</param>
    public ReportEmailService(ITestReportExporter exporter, IEmailSender emailSender)
    {
        _exporter = exporter;
        _emailSender = emailSender;
    }

    /// <inheritdoc />
    public async Task SendReportAsync(
        TestReportData report,
        string recipient,
        bool includeCertificate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        var text = _exporter.GenerateText(report);
        var html = includeCertificate
            ? _exporter.GenerateCertificateHtml(report)
            : _exporter.GenerateHtml(report);

        var message = new EmailMessage
        {
            ToAddress = recipient,
            Subject = "DiskChecker report",
            TextBody = text,
            HtmlBody = html
        };

        await _emailSender.SendAsync(message, cancellationToken);
    }
}
