using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DiskChecker.Application.Services;

/// <summary>
/// Sends email messages using SMTP.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly IEmailSettingsService _settingsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmtpEmailSender"/> class.
    /// </summary>
    /// <param name="settingsService">Settings service.</param>
    public SmtpEmailSender(IEmailSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Resolves SMTP transport security. Plain SMTP is used only if explicitly selected.
    /// </summary>
    public static SecureSocketOptions GetSocketOptions(EmailSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !settings.UseSsl
            ? SecureSocketOptions.None
            : settings.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;
    }

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = await _settingsService.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            throw new InvalidOperationException("SMTP settings are not configured.");
        }

        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        email.To.Add(MailboxAddress.Parse(message.ToAddress));
        email.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder
        {
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody
        };

        foreach (var attachment in message.Attachments)
        {
            if (attachment == null || attachment.Content.Length == 0 || string.IsNullOrWhiteSpace(attachment.FileName))
            {
                continue;
            }

            bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        email.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        // TLS is mandatory when selected: do not silently fall back to plaintext.
        // For an explicitly configured intranet relay, TLS can be turned off.
        var socketOptions = GetSocketOptions(settings);
        await client.ConnectAsync(settings.Host, settings.Port, socketOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.UserName))
        {
            await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
        }

        await client.SendAsync(email, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
