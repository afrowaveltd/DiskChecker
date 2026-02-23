using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Provides persistence for SMTP settings.
/// </summary>
public interface IEmailSettingsService
{
    /// <summary>
    /// Loads the stored SMTP settings.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Stored settings.</returns>
    Task<EmailSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves SMTP settings for future use.
    /// </summary>
    /// <param name="settings">Settings to store.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SaveAsync(EmailSettings settings, CancellationToken cancellationToken = default);
}
