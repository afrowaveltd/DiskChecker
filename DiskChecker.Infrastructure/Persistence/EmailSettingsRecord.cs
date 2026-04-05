using System.ComponentModel.DataAnnotations;

namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// Database record for email settings.
/// </summary>
public class EmailSettingsRecord
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// SMTP server host name or IP address.
    /// </summary>
    [MaxLength(200)]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// SMTP server port number.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Whether to use SSL/TLS encryption.
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// Username for authentication.
    /// </summary>
    [MaxLength(200)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Password for authentication.
    /// </summary>
    [MaxLength(500)]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Sender name (e.g. "DiskChecker").
    /// </summary>
    [MaxLength(100)]
    public string? FromName { get; set; }

    /// <summary>
    /// Sender email address.
    /// </summary>
    [MaxLength(200)]
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// When settings were last modified.
    /// </summary>
    public DateTime? UpdatedAtUtc { get; set; }
    
    /// <summary>
    /// Legacy property for backward compatibility.
    /// </summary>
    [MaxLength(200)]
    public string SmtpServer 
    { 
        get => Host;
        set => Host = value;
    }
    
    /// <summary>
    /// Legacy property for backward compatibility.
    /// </summary>
    public int SmtpPort 
    { 
        get => Port;
        set => Port = value;
    }
    
    /// <summary>
    /// Legacy property for backward compatibility.
    /// </summary>
    public bool EnableSsl 
    { 
        get => UseSsl;
        set => UseSsl = value;
    }

    /// <summary>
    /// Whether completion e-mails should include the PDF certificate attachment.
    /// </summary>
    public bool IncludeCertificateAttachment { get; set; } = true;

    /// <summary>
    /// Legacy property for backward compatibility.
    /// </summary>
    public bool AttachCertificate 
    { 
        get => IncludeCertificateAttachment;
        set => IncludeCertificateAttachment = value;
    }
}