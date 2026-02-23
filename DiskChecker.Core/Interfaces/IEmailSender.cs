using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Defines SMTP email sending.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends an email using the configured SMTP settings.
    /// </summary>
    /// <param name="message">Message data.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
