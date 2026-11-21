using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DiskChecker.Application.Services;

/// <summary>
/// Sends SMTP messages with mandatory, validated TLS by default.
/// Plaintext SMTP requires an explicit, persisted opt-in; TLS is never
/// downgraded automatically. Trust private CAs via the OS certificate store.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IEmailSettingsService _settingsService;

    /// <summary>Initializes the sender with its persisted SMTP configuration source.</summary>
    public SmtpEmailSender(IEmailSettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    /// <summary>Determines a single, strictly enforced SMTP transport policy.</summary>
    public static SecureSocketOptions SecurityOption(EmailSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Port, "SMTP port must be 1–65535.");
        if (!settings.UseSsl && !settings.AllowInsecureSmtp)
            throw new InvalidOperationException(
                "Plaintext SMTP is disabled. Explicitly enable 'Allow insecure SMTP server' in settings.");

        // Even when the opt-in exists, TLS remains required as long as UseSsl is true.
        // A certificate or STARTTLS error is NEVER a reason to fall back to plaintext.
        // Port 465 is implicit TLS; other ports require explicit STARTTLS.
        return settings.UseSsl
            ? (settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
            : SecureSocketOptions.None;
    }

    /// <inheritdoc/>
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = await _settingsService.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException("SMTP host and sender address must be configured.");
        var option = SecurityOption(settings);

        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        email.To.Add(MailboxAddress.Parse(message.ToAddress));
        email.Subject = message.Subject;
        var body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody };
        foreach (var attachment in message.Attachments)
        {
            if (attachment.Content.Length == 0 || string.IsNullOrWhiteSpace(attachment.FileName))
                throw new InvalidOperationException("Email attachment is empty or lacks a file name.");
            body.Attachments.Add(attachment.FileName, attachment.Content,
                ContentType.Parse(attachment.ContentType));
        }
        email.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        // Default trusted chain + hostname validation. No callback override.
        await client.ConnectAsync(settings.Host, settings.Port, option, cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.UserName))
            await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
        await client.SendAsync(email, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
