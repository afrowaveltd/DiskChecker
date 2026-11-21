using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Net;

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

        // TLS is authenticated with the platform trust store; do not bypass
        // certificate validation and never silently fall back to plaintext.
        var security = ResolveSocketSecurity(settings);
        if (security == SecureSocketOptions.None &&
            !string.IsNullOrWhiteSpace(settings.UserName) && !IsLoopback(settings.Host))
        {
            throw new InvalidOperationException(
                "SMTP authentication without TLS is refused for non-loopback servers.");
        }

        using var client = new SmtpClient { Timeout = 20000 };
        await client.ConnectAsync(settings.Host, settings.Port, security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.UserName))
        {
            await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
        }

        await client.SendAsync(email, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
    /// <summary>Applies one explicit, non-downgrading transport policy.</summary>
    public static SecureSocketOptions ResolveSocketSecurity(EmailSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(settings), "SMTP port must be in range 1–65535.");
        if (!settings.UseSsl)
            return SecureSocketOptions.None;
        return settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
    }

    private static bool IsLoopback(string host)
    {
        if (string.Equals(host.Trim(), "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

}
