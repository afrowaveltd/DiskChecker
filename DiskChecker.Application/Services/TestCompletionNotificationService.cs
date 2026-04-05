using DiskChecker.Core.Models;
using DiskChecker.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Text;

namespace DiskChecker.Application.Services;

/// <summary>
/// Service for sending test completion notifications via email.
/// </summary>
public partial class TestCompletionNotificationService
{
    private readonly IEmailSender _emailSender;
    private readonly IEmailSettingsService _emailSettingsService;
    private readonly ILogger<TestCompletionNotificationService> _logger;

    // LoggerMessage delegates for better performance
    [LoggerMessage(Level = LogLevel.Warning, Message = "Email address is empty, skipping notification")]
    private partial void LogEmptyEmail();

    [LoggerMessage(Level = LogLevel.Information, Message = "Test completion notification sent to {RecipientEmail}")]
    private partial void LogNotificationSent(string recipientEmail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send test completion notification to {RecipientEmail}")]
    private partial void LogNotificationFailed(Exception ex, string recipientEmail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Test completion notification with report sent to {RecipientEmail}")]
    private partial void LogNotificationWithReportSent(string recipientEmail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send test completion notification with report to {RecipientEmail}")]
    private partial void LogNotificationWithReportFailed(Exception ex, string recipientEmail);

    public TestCompletionNotificationService(
        IEmailSender emailSender,
        IEmailSettingsService emailSettingsService,
        ILogger<TestCompletionNotificationService> logger)
    {
        _emailSender = emailSender;
        _emailSettingsService = emailSettingsService;
        _logger = logger;
    }

    /// <summary>
    /// Sends a test completion notification email.
    /// </summary>
    public async Task SendTestCompletionNotificationAsync(
        SurfaceTestResult result,
        string recipientEmail,
        string? diskName = null,
        CancellationToken cancellationToken = default)
    {
        var emailSettings = await _emailSettingsService.GetAsync(cancellationToken);
        var resolvedRecipient = ResolveRecipient(recipientEmail, emailSettings.FromAddress);
        if (string.IsNullOrWhiteSpace(resolvedRecipient))
        {
            LogEmptyEmail();
            return;
        }

        try
        {
            var subject = $"🔍 Test disku {diskName ?? "Unknown"} - {GetResultStatus(result)}";
            var body = BuildEmailBody(result, diskName);

            var message = new EmailMessage
            {
                ToAddress = resolvedRecipient,
                Subject = subject,
                HtmlBody = body
            };

            await _emailSender.SendAsync(message, cancellationToken);

            LogNotificationSent(resolvedRecipient);
        }
        catch (InvalidOperationException ex)
        {
            LogNotificationFailed(ex, resolvedRecipient);
        }
    }

    /// <summary>
    /// Sends a test completion notification with attachment.
    /// </summary>
    public async Task SendTestCompletionWithReportAsync(
        SurfaceTestResult result,
        string recipientEmail,
        byte[]? reportPdf,
        string? diskName = null,
        DiskCertificate? certificate = null,
        string? attachmentFileName = null,
        CancellationToken cancellationToken = default)
    {
        var emailSettings = await _emailSettingsService.GetAsync(cancellationToken);
        var resolvedRecipient = ResolveRecipient(recipientEmail, emailSettings.FromAddress);
        if (string.IsNullOrWhiteSpace(resolvedRecipient))
        {
            LogEmptyEmail();
            return;
        }

        try
        {
            var subject = $"📊 Test disku {diskName ?? "Unknown"} - Zpráva - {GetResultStatus(result)}";
            var body = certificate != null
                ? BuildCertificateEmailBody(certificate, result)
                : BuildEmailBody(result, diskName, includeReport: true);

            var message = new EmailMessage
            {
                ToAddress = resolvedRecipient,
                Subject = subject,
                HtmlBody = body
            };

            if (emailSettings.IncludeCertificateAttachment && reportPdf is { Length: > 0 })
            {
                message.Attachments.Add(new EmailAttachment
                {
                    FileName = string.IsNullOrWhiteSpace(attachmentFileName)
                        ? $"DiskCertificate_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf"
                        : attachmentFileName,
                    Content = reportPdf,
                    ContentType = "application/pdf"
                });
            }

            await _emailSender.SendAsync(message, cancellationToken);

            LogNotificationWithReportSent(resolvedRecipient);
        }
        catch (InvalidOperationException ex)
        {
            LogNotificationWithReportFailed(ex, resolvedRecipient);
        }
    }

    private string GetResultStatus(SurfaceTestResult result)
    {
        return result.ErrorCount == 0 ? "✅ ÚSPĚŠNO" : $"⚠️ CHYBY ({result.ErrorCount})";
    }

    private string BuildEmailBody(SurfaceTestResult result, string? diskName, bool includeReport = false)
    {
        var status = result.ErrorCount == 0 ? "ÚSPĚŠNÝ" : "S CHYBAMI";
        var testDuration = result.CompletedAtUtc != default && result.StartedAtUtc != default
            ? (result.CompletedAtUtc - result.StartedAtUtc).TotalMinutes
            : 0;

        var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <style>
        body {{ font-family: Arial, sans-serif; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #4A90E2; color: white; padding: 20px; border-radius: 8px; text-align: center; margin-bottom: 20px; }}
        .status {{ font-size: 18px; font-weight: bold; margin: 10px 0; }}
        .success {{ color: #28a745; }}
        .error {{ color: #dc3545; }}
        .content {{ background: #f9f9f9; padding: 20px; border-radius: 8px; }}
        .stat-row {{ display: grid; grid-template-columns: 1fr 1fr; margin: 10px 0; padding: 10px; background: white; border-radius: 4px; }}
        .stat-label {{ font-weight: bold; }}
        .footer {{ margin-top: 20px; font-size: 12px; color: #999; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🔍 Zpráva o Testu Disku</h1>
            <div class='status {(result.ErrorCount == 0 ? "success" : "error")}'>
                {status}
            </div>
        </div>

        <div class='content'>
            <h2>Informace o Testu</h2>
            {(!string.IsNullOrEmpty(diskName) ? $"<div class='stat-row'><div class='stat-label'>Disk:</div><div>{diskName}</div></div>" : "")}
            <div class='stat-row'><div class='stat-label'>Profil:</div><div>{result.Profile}</div></div>
            <div class='stat-row'><div class='stat-label'>Trvání:</div><div>{testDuration:F0} minut</div></div>
            <div class='stat-row'><div class='stat-label'>Čas začátku:</div><div>{result.StartedAtUtc:dd.MM.yyyy HH:mm:ss}</div></div>

            <h2>Výsledky</h2>
            <div class='stat-row'><div class='stat-label'>Testováno:</div><div>{FormatBytes(result.TotalBytesTested)}</div></div>
            <div class='stat-row'><div class='stat-label'>Průměrná Rychlost:</div><div>{result.AverageSpeedMbps:F1} MB/s</div></div>
            <div class='stat-row'><div class='stat-label'>Max. Rychlost:</div><div>{result.PeakSpeedMbps:F1} MB/s</div></div>
            <div class='stat-row'><div class='stat-label'>Min. Rychlost:</div><div>{result.MinSpeedMbps:F1} MB/s</div></div>
            <div class='stat-row'><div class='stat-label'>Chyby:</div><div {(result.ErrorCount > 0 ? "class='error'" : "")}>{result.ErrorCount}</div></div>
            <div class='stat-row'><div class='stat-label'>Vzorky:</div><div>{result.Samples.Count}</div></div>

            {(!string.IsNullOrEmpty(result.Notes) ? $"<h2>Poznámky</h2><p>{result.Notes}</p>" : "")}

            {(includeReport ? "<p><strong>Podrobnější zpráva je v příloze PDF.</strong></p>" : "")}
        </div>

        <div class='footer'>
            <p>Tato zpráva byla automaticky vygenerována aplikací DiskChecker.</p>
            <p>Pokud máte dotazy, kontaktujte svého správce.</p>
        </div>
    </div>
</body>
</html>";

        return body;
    }

    private static string BuildCertificateEmailBody(DiskCertificate certificate, SurfaceTestResult result)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8' /><style>");
        sb.Append("body{font-family:Segoe UI,Arial,sans-serif;color:#1f2937;background:#f8fafc;margin:0;padding:16px;}");
        sb.Append(".container{max-width:860px;margin:0 auto;background:#fff;border:1px solid #dbe3ee;border-radius:12px;padding:20px;}");
        sb.Append(".header{background:#2A6FD6;color:#fff;padding:16px;border-radius:10px;margin-bottom:16px;}");
        sb.Append(".grid{display:grid;grid-template-columns:1fr 1fr;gap:12px;}");
        sb.Append(".panel{background:#f6f8fb;border:1px solid #e5eaf1;border-radius:8px;padding:12px;}");
        sb.Append(".row{display:grid;grid-template-columns:220px 1fr;gap:8px;padding:4px 0;}");
        sb.Append(".label{font-weight:600;color:#374151;}");
        sb.Append(".value{color:#111827;}");
        sb.Append(".title{font-size:15px;font-weight:700;margin-bottom:8px;color:#0f172a;}");
        sb.Append(".grade{font-size:28px;font-weight:800;margin:0;color:#1f2937;}");
        sb.Append(".foot{margin-top:14px;font-size:12px;color:#6b7280;}");
        sb.Append("</style></head><body><div class='container'>");
        sb.Append("<div class='header'><h2 style='margin:0'>📄 Certifikát kvality disku</h2><div style='margin-top:6px'>DiskChecker - kompletní souhrn výsledků</div></div>");

        sb.Append("<div class='grid'>");
        sb.Append("<div class='panel'><div class='title'>Identita disku</div>");
        AppendRow(sb, "Model", certificate.DiskModel);
        AppendRow(sb, "Sériové číslo", certificate.SerialNumber);
        AppendRow(sb, "Kapacita", certificate.Capacity);
        AppendRow(sb, "Typ disku", certificate.DiskType);
        AppendRow(sb, "Rozhraní", certificate.Interface);
        AppendRow(sb, "Firmware", certificate.Firmware);
        AppendRow(sb, "Provozní hodiny", certificate.PowerOnHours > 0 ? certificate.PowerOnHours.ToString("#,0", CultureInfo.InvariantCulture) : "N/A");
        AppendRow(sb, "Počet startů", certificate.PowerCycles > 0 ? certificate.PowerCycles.ToString("#,0", CultureInfo.InvariantCulture) : "N/A");
        AppendRow(sb, "Číslo certifikátu", certificate.CertificateNumber);
        AppendRow(sb, "Generováno", certificate.GeneratedAt.ToString("dd.MM.yyyy HH:mm"));
        sb.Append("</div>");

        sb.Append("<div class='panel'><div class='title'>Hodnocení</div>");
        sb.Append($"<p class='grade'>Známka {WebUtility.HtmlEncode(certificate.Grade)} - {certificate.Score:F0}/100</p>");
        AppendRow(sb, "Stav", certificate.HealthStatus);
        AppendRow(sb, "Typ testu", certificate.TestType);
        AppendRow(sb, "Délka testu", certificate.TestDuration.ToString(@"hh\:mm\:ss"));
        AppendRow(sb, "SMART", certificate.SmartPassed ? "✅ V pořádku" : "⚠️ Varování");
        AppendRow(sb, "Doporučení", certificate.Recommended ? "✅ Doporučeno" : "⚠️ Nedoporučeno");
        sb.Append("</div>");
        sb.Append("</div>");

        sb.Append("<div class='panel' style='margin-top:12px'><div class='title'>Výkon testu</div>");
        AppendRow(sb, "Průměrný zápis", $"{certificate.AvgWriteSpeed:F1} MB/s");
        AppendRow(sb, "Maximální zápis", $"{certificate.MaxWriteSpeed:F1} MB/s");
        AppendRow(sb, "Průměrné čtení", $"{certificate.AvgReadSpeed:F1} MB/s");
        AppendRow(sb, "Maximální čtení", $"{certificate.MaxReadSpeed:F1} MB/s");
        AppendRow(sb, "Rozsah teplot", certificate.TemperatureRange);
        AppendRow(sb, "Počet chyb", certificate.ErrorCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Vzorky", result.Samples.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append("</div>");

        sb.Append("<div class='panel' style='margin-top:12px'><div class='title'>SMART souhrn</div>");
        AppendRow(sb, "Realokované sektory", certificate.ReallocatedSectors.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Čekající sektory", certificate.PendingSectors.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(certificate.Notes))
        {
            AppendRow(sb, "Poznámky", certificate.Notes!);
        }
        sb.Append("</div>");

        sb.Append("<div class='foot'>Tento e-mail obsahuje kompletní certifikační souhrn. PDF příloha je volitelná dle nastavení.</div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string label, string? value)
    {
        sb.Append("<div class='row'>");
        sb.Append($"<div class='label'>{WebUtility.HtmlEncode(label)}:</div>");
        sb.Append($"<div class='value'>{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "N/A" : value)}</div>");
        sb.Append("</div>");
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double b = bytes;
        while (b >= 1024 && i < sizes.Length - 1)
        {
            b /= 1024;
            i++;
        }
        return $"{b:F1} {sizes[i]}";
    }

    private static string? ResolveRecipient(string? preferredRecipient, string? fallbackRecipient)
    {
        if (!string.IsNullOrWhiteSpace(preferredRecipient))
        {
            return preferredRecipient.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallbackRecipient))
        {
            return fallbackRecipient.Trim();
        }

        return null;
    }
}
