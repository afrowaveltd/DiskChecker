using System.Text;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Generates certificate text from SMART data and quality rating.
/// </summary>
public static class CertificateGenerator
{
    /// <summary>
    /// Generates a certificate text from quality rating and SMART data.
    /// </summary>
    /// <param name="rating">Quality rating.</param>
    /// <param name="smartaData">SMART data.</param>
    /// <param name="testDate">Test date.</param>
    /// <returns>Certificate text.</returns>
    public static string GenerateCertificate(this QualityRating rating, SmartaData smartaData, DateTime testDate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("════════════════════════════════════════════════════════════════");
        sb.AppendLine("                    DISK HEALTH CERTIFICATE                      ");
        sb.AppendLine("════════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Test Date: {testDate:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Device Model: {smartaData.DeviceModel ?? "Unknown"}");
        sb.AppendLine($"Serial Number: {smartaData.SerialNumber ?? "Unknown"}");
        sb.AppendLine($"Firmware: {smartaData.FirmwareVersion ?? "Unknown"}");
        sb.AppendLine();
        sb.AppendLine("────────────────────────────────────────────────────────────────");
        sb.AppendLine("                       HEALTH ASSESSMENT                         ");
        sb.AppendLine("────────────────────────────────────────────────────────────────");
        sb.AppendLine($"Grade: {rating.Grade}");
        sb.AppendLine($"Score: {rating.Score:F1}/100");
        sb.AppendLine();

        if (rating.Warnings.Count > 0)
        {
            sb.AppendLine("Warnings:");
            foreach (var warning in rating.Warnings)
            {
                sb.AppendLine($"  • {warning}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("────────────────────────────────────────────────────────────────");
        sb.AppendLine("                       SMART ATTRIBUTES                          ");
        sb.AppendLine("────────────────────────────────────────────────────────────────");
        
        if (smartaData.PowerOnHours.HasValue)
            sb.AppendLine($"Power-On Hours: {smartaData.PowerOnHours.Value} h");
        if (smartaData.Temperature.HasValue)
            sb.AppendLine($"Temperature: {smartaData.Temperature.Value} °C");
        if (smartaData.ReallocatedSectorCount.HasValue)
            sb.AppendLine($"Reallocated Sectors: {smartaData.ReallocatedSectorCount.Value}");
        if (smartaData.PendingSectorCount.HasValue)
            sb.AppendLine($"Pending Sectors: {smartaData.PendingSectorCount.Value}");
        if (smartaData.UncorrectableErrorCount.HasValue)
            sb.AppendLine($"Uncorrectable Errors: {smartaData.UncorrectableErrorCount.Value}");
        if (smartaData.WearLevelingCount.HasValue)
            sb.AppendLine($"Wear Leveling: {smartaData.WearLevelingCount.Value}%");

        sb.AppendLine();
        sb.AppendLine("════════════════════════════════════════════════════════════════");
        sb.AppendLine("            This certificate is machine-generated.               ");
        sb.AppendLine("════════════════════════════════════════════════════════════════");

        return sb.ToString();
    }
}