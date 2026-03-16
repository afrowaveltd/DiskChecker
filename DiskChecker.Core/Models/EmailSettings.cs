using System.Collections.Generic;

namespace DiskChecker.Core.Models;

/// <summary>
/// Represents SMTP settings for sending notifications.
/// </summary>
public class EmailSettings
{
    /// <summary>
    /// Gets or sets the SMTP host name.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SMTP port.
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Gets or sets whether SSL should be used.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// Gets or sets the SMTP username.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SMTP password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the sender.
    /// </summary>
    public string FromName { get; set; } = "DiskChecker";

    /// <summary>
    /// Gets or sets the sender email address.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;
}

/// <summary>
/// Represents a request to send an email message.
/// </summary>
public class EmailMessage
{
    /// <summary>
    /// Gets or sets the recipient email address.
    /// </summary>
    public string ToAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plain text body.
    /// </summary>
    public string? TextBody { get; set; }

    /// <summary>
    /// Gets or sets the HTML body.
    /// </summary>
    public string? HtmlBody { get; set; }

    /// <summary>
    /// Gets the attachments included in this message.
    /// </summary>
    public List<EmailAttachment> Attachments { get; } = new();
}

/// <summary>
/// Represents a binary email attachment.
/// </summary>
public class EmailAttachment
{
    /// <summary>
    /// Gets or sets the attachment file name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the attachment content bytes.
    /// </summary>
    public byte[] Content { get; set; } = [];

    /// <summary>
    /// Gets or sets the MIME content type.
    /// </summary>
    public string ContentType { get; set; } = "application/octet-stream";
}
