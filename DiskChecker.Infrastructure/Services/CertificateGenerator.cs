using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// Service for generating disk certificates and PDFs.
/// </summary>
[SupportedOSPlatform("windows")]
public class CertificateGenerator : ICertificateGenerator
{
    private readonly ILogger<CertificateGenerator>? _logger;
    private readonly string _certificatesDirectory;
    private readonly string _labelsDirectory;

    public CertificateGenerator(ILogger<CertificateGenerator>? logger = null)
    {
        _logger = logger;
        _certificatesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiskChecker",
            "Certificates");
        _labelsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiskChecker",
            "Labels");
        
        Directory.CreateDirectory(_certificatesDirectory);
        Directory.CreateDirectory(_labelsDirectory);
    }

    public async Task<DiskCertificate> GenerateCertificateAsync(TestSession session, DiskCard diskCard)
    {
        var certificate = new DiskCertificate
        {
            DiskCardId = diskCard.Id,
            TestSessionId = session.Id,
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = Environment.UserName,
            
            // Disk information
            DiskModel = diskCard.ModelName,
            SerialNumber = diskCard.SerialNumber,
            Capacity = FormatCapacity(diskCard.Capacity),
            DiskType = diskCard.DiskType,
            Firmware = diskCard.FirmwareVersion,
            Interface = diskCard.InterfaceType,
            
            // Test results
            TestType = session.TestType.ToString(),
            TestDuration = session.Duration,
            ErrorCount = session.Errors.Count,
            
            // Performance metrics
            AvgWriteSpeed = session.AverageWriteSpeedMBps,
            MaxWriteSpeed = session.MaxWriteSpeedMBps,
            AvgReadSpeed = session.AverageReadSpeedMBps,
            MaxReadSpeed = session.MaxReadSpeedMBps,
            TemperatureRange = session.StartTemperature.HasValue && session.MaxTemperature.HasValue
                ? $"{session.StartTemperature.Value}°C - {session.MaxTemperature.Value}°C"
                : "N/A",
            
            // SMART summary
            SmartPassed = session.SmartBefore?.IsHealthy ?? true,
            PowerOnHours = session.SmartBefore?.PowerOnHours ?? 0,
            PowerCycles = session.SmartBefore?.PowerCycleCount ?? 0,
            ReallocatedSectors = session.SmartBefore?.ReallocatedSectorCount ?? 0,
            PendingSectors = session.SmartBefore?.PendingSectorCount ?? 0,
            
            // Certificate status
            SanitizationPerformed = session.TestType == TestType.Sanitization,
            SanitizationMethod = session.TestType == TestType.Sanitization ? "Zero-fill" : null,
            DataVerified = session.VerificationErrors == 0,
            PartitionScheme = session.PartitionScheme,
            FileSystem = session.FileSystem,
            VolumeLabel = session.VolumeLabel,
            Status = CertificateStatus.Active,
            
            // Recommendation
            Recommended = session.Result == TestResult.Pass && session.Score >= 70,
            RecommendationNotes = GenerateRecommendation(session),
            Notes = session.Notes
        };

        // Calculate grade and score using shared method
        var (grade, score) = CalculateGrade(session);
        certificate.Grade = grade;
        certificate.Score = score;
        certificate.HealthStatus = session.HealthAssessment.ToString();

        // Generate SMART attribute summary
        if (session.SmartBefore?.Attributes != null)
        {
            foreach (var attr in session.SmartBefore.Attributes)
            {
                if (IsCriticalAttribute(attr.Id))
                {
                    certificate.SmartAttributes.Add(new SmartAttributeSummary
                    {
                        Id = attr.Id,
                        Name = attr.Name,
                        Value = attr.RawValue.ToString(),
                        Status = attr.IsOk ? "OK" : "Warning",
                        IsCritical = true
                    });
                }
            }
        }

        return certificate;
    }

    public async Task<string> GeneratePdfAsync(DiskCertificate certificate)
    {
        var fileName = $"Certificate_{certificate.CertificateNumber}.pdf";
        var filePath = Path.Combine(_certificatesDirectory, fileName);

        // For now, generate a simple PDF-like report
        // In production, use a proper PDF library like iTextSharp or PdfSharp
        var reportContent = GenerateTextReport(certificate);
        await File.WriteAllTextAsync(filePath, reportContent);

        certificate.PdfGenerated = true;
        certificate.PdfPath = filePath;

        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Certificate PDF generated: {FilePath}", filePath);
        }

        return filePath;
    }

    /// <summary>
    /// Generates a printable disk label image from certificate data.
    /// </summary>
    public async Task<string> GenerateLabelAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var safeSerial = string.IsNullOrWhiteSpace(certificate.SerialNumber)
            ? "UNKNOWN"
            : certificate.SerialNumber.Replace(Path.DirectorySeparatorChar, '-').Replace(Path.AltDirectorySeparatorChar, '-');

        var fileName = $"Label_{safeSerial}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";
        var filePath = Path.Combine(_labelsDirectory, fileName);

        await Task.Run(() =>
        {
            using var bitmap = new Bitmap(900, 520);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);

            using var borderPen = new Pen(Color.FromArgb(15, 76, 129), 3);
            graphics.DrawRectangle(borderPen, 10, 10, 880, 500);

            using var headerBrush = new SolidBrush(Color.FromArgb(15, 76, 129));
            using var textBrush = new SolidBrush(Color.Black);
            using var mutedBrush = new SolidBrush(Color.FromArgb(90, 90, 90));
            using var headerFont = new Font("Segoe UI", 28, FontStyle.Bold);
            using var valueFont = new Font("Segoe UI", 18, FontStyle.Regular);
            using var labelFont = new Font("Segoe UI", 12, FontStyle.Bold);
            using var serialFont = new Font("Consolas", 16, FontStyle.Bold);
            using var gradeFont = new Font("Segoe UI", 90, FontStyle.Bold);
            using var scoreFont = new Font("Segoe UI", 18, FontStyle.Bold);
            using var footerFont = new Font("Segoe UI", 10, FontStyle.Regular);
            using var gradeBrush = new SolidBrush(GetGradeColor(certificate.Grade));

            graphics.DrawString("DISK LABEL", headerFont, headerBrush, 28, 24);

            graphics.DrawString("Model", labelFont, mutedBrush, 32, 98);
            graphics.DrawString(TrimValue(certificate.DiskModel, 34), valueFont, textBrush, 32, 120);

            graphics.DrawString("Sériové číslo", labelFont, mutedBrush, 32, 170);
            graphics.DrawString(TrimValue(certificate.SerialNumber, 40), serialFont, textBrush, 32, 194);

            graphics.DrawString("Kapacita", labelFont, mutedBrush, 32, 244);
            graphics.DrawString(TrimValue(certificate.Capacity, 20), valueFont, textBrush, 32, 266);

            graphics.DrawString("Rozhraní", labelFont, mutedBrush, 32, 314);
            graphics.DrawString(TrimValue(certificate.Interface, 20), valueFont, textBrush, 32, 336);

            graphics.DrawString("Test", labelFont, mutedBrush, 32, 384);
            graphics.DrawString(TrimValue(certificate.TestType, 24), valueFont, textBrush, 32, 406);

            graphics.DrawString(certificate.Grade, gradeFont, gradeBrush, 680, 90);
            graphics.DrawString($"Skóre: {certificate.Score:F0}/100", scoreFont, textBrush, 652, 220);
            graphics.DrawString($"{certificate.GeneratedAt:dd.MM.yyyy}", scoreFont, textBrush, 675, 255);

            graphics.DrawString(
                $"Certifikát: {certificate.CertificateNumber}",
                footerFont,
                mutedBrush,
                32,
                468);
            graphics.DrawString("DiskChecker", footerFont, mutedBrush, 805, 468);

            bitmap.Save(filePath, ImageFormat.Png);
        });

        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Disk label generated: {FilePath}", filePath);
        }

        return filePath;
    }

    public async Task<byte[]> GeneratePreviewAsync(DiskCertificate certificate)
    {
        // Generate a simple image preview
        // In production, use proper graphics rendering
        await Task.Delay(100); // Simulate rendering

        using var bitmap = new Bitmap(800, 1000);
        using var graphics = Graphics.FromImage(bitmap);
        
        // Fill background
        graphics.Clear(Color.White);
        
        // Draw border
        using var borderPen = new Pen(Color.Navy, 3);
        graphics.DrawRectangle(borderPen, 10, 10, 780, 980);
        
        // Draw header
        using var headerBrush = new SolidBrush(Color.Navy);
        using var headerFont = new Font("Arial", 24, FontStyle.Bold);
        graphics.DrawString("DISK CERTIFICATE", headerFont, headerBrush, 250, 50);
        
        using var subFont = new Font("Arial", 12);
        using var labelFont = new Font("Arial", 10, FontStyle.Bold);
        using var valueFont = new Font("Arial", 12);
        using var gradeFont = new Font("Arial", 48, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.Black);
        using var gradeBrush = new SolidBrush(GetGradeColor(certificate.Grade));

        // Certificate number
        graphics.DrawString($"Certificate: {certificate.CertificateNumber}", subFont, textBrush, 30, 100);
        graphics.DrawString($"Generated: {certificate.GeneratedAt:yyyy-MM-dd HH:mm}", subFont, textBrush, 30, 120);

        // Disk information
        int y = 160;
        var labels = new Dictionary<string, string>
        {
            ["Model"] = certificate.DiskModel,
            ["Serial Number"] = certificate.SerialNumber,
            ["Capacity"] = certificate.Capacity,
            ["Type"] = certificate.DiskType,
            ["Interface"] = certificate.Interface,
            ["Firmware"] = certificate.Firmware
        };

        foreach (var (label, value) in labels)
        {
            graphics.DrawString($"{label}:", labelFont, textBrush, 30, y);
            graphics.DrawString(value, valueFont, textBrush, 200, y);
            y += 25;
        }

        // Test results
        y += 20;
        graphics.DrawString("TEST RESULTS", headerFont, headerBrush, 30, y);
        y += 40;

        graphics.DrawString($"Test Type: {certificate.TestType}", valueFont, textBrush, 30, y);
        graphics.DrawString($"Duration: {certificate.TestDuration:hh\\:mm\\:ss}", valueFont, textBrush, 30, y + 25);
        graphics.DrawString($"Errors: {certificate.ErrorCount}", valueFont, textBrush, 30, y + 50);

        // Performance metrics
        y += 90;
        graphics.DrawString("PERFORMANCE", headerFont, headerBrush, 30, y);
        y += 40;

        graphics.DrawString($"Write Speed: {certificate.AvgWriteSpeed:F1} MB/s (max: {certificate.MaxWriteSpeed:F1})", valueFont, textBrush, 30, y);
        graphics.DrawString($"Read Speed: {certificate.AvgReadSpeed:F1} MB/s (max: {certificate.MaxReadSpeed:F1})", valueFont, textBrush, 30, y + 25);
        graphics.DrawString($"Temperature: {certificate.TemperatureRange}", valueFont, textBrush, 30, y + 50);

        // Grade (large)
        y += 100;
        var gradeText = certificate.Grade;
        var gradeSize = graphics.MeasureString(gradeText, gradeFont);
        graphics.DrawString(gradeText, gradeFont, gradeBrush, 
            (800 - gradeSize.Width) / 2, y);

        // Score
        y += 80;
        graphics.DrawString($"Score: {certificate.Score:F0}/100", new Font("Arial", 16), textBrush,
            (800 - 200) / 2, y);
        graphics.DrawString($"Health: {certificate.HealthStatus}", new Font("Arial", 14), textBrush,
            (800 - 200) / 2, y + 30);

        // Recommendation
        if (certificate.Recommended)
        {
            y += 70;
            using var recFont = new Font("Arial", 14, FontStyle.Bold);
            using var recBrush = new SolidBrush(Color.Green);
            graphics.DrawString("✓ RECOMMENDED", recFont, recBrush, 300, y);
        }

        // Footer
        using var footerFont = new Font("Arial", 10);
        graphics.DrawString("Generated by DiskChecker - Professional Disk Testing Solution", footerFont, textBrush, 200, 950);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    public (string grade, double score) CalculateGrade(TestSession session)
    {
        double score = 100;
        
        // Deduct points for errors
        score -= session.WriteErrors * 5;
        score -= session.ReadErrors * 5;
        score -= session.VerificationErrors * 10;
        
        // Deduct points for high temperatures
        if (session.MaxTemperature.HasValue && session.MaxTemperature > 60)
        {
            score -= (session.MaxTemperature.Value - 60) * 2;
        }
        
        // Deduct points for slow speeds
        if (session.AverageWriteSpeedMBps < 50)
        {
            score -= (50 - session.AverageWriteSpeedMBps) * 0.5;
        }
        
        if (session.AverageReadSpeedMBps < 50)
        {
            score -= (50 - session.AverageReadSpeedMBps) * 0.5;
        }
        
        score = Math.Max(0, Math.Min(100, score));
        
        string grade = score switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 60 => "D",
            >= 50 => "E",
            _ => "F"
        };
        
        return (grade, score);
    }

    private string GenerateTextReport(DiskCertificate cert)
    {
        return $@"
================================================================================
                         DISK CERTIFICATE
================================================================================

Certificate Number: {cert.CertificateNumber}
Generated: {cert.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC
Generated By: {cert.GeneratedBy}

--------------------------------------------------------------------------------
                              DISK INFORMATION
--------------------------------------------------------------------------------
Model:           {cert.DiskModel}
Serial Number:   {cert.SerialNumber}
Capacity:        {cert.Capacity}
Type:            {cert.DiskType}
Interface:       {cert.Interface}
Firmware:        {cert.Firmware}

--------------------------------------------------------------------------------
                              TEST RESULTS
--------------------------------------------------------------------------------
Test Type:       {cert.TestType}
Duration:        {cert.TestDuration:hh\:mm\:ss}
Errors:          {cert.ErrorCount}

Grade:           {cert.Grade}
Score:           {cert.Score:F0}/100
Health Status:   {cert.HealthStatus}

--------------------------------------------------------------------------------
                            PERFORMANCE METRICS
--------------------------------------------------------------------------------
Write Speed:     {cert.AvgWriteSpeed:F1} MB/s (max: {cert.MaxWriteSpeed:F1} MB/s)
Read Speed:      {cert.AvgReadSpeed:F1} MB/s (max: {cert.MaxReadSpeed:F1} MB/s)
Temperature:     {cert.TemperatureRange}

--------------------------------------------------------------------------------
                              SMART SUMMARY
--------------------------------------------------------------------------------
SMART Passed:    {(cert.SmartPassed ? "Yes" : "No")}
Power On Hours:  {cert.PowerOnHours}
Power Cycles:    {cert.PowerCycles}
Reallocated:     {cert.ReallocatedSectors}
Pending Sectors: {cert.PendingSectors}

--------------------------------------------------------------------------------
                            SANITIZATION
--------------------------------------------------------------------------------
Performed:       {(cert.SanitizationPerformed ? "Yes" : "No")}
Method:          {cert.SanitizationMethod ?? "N/A"}
Data Verified:   {(cert.DataVerified ? "Yes" : "No")}
Partition:       {cert.PartitionScheme ?? "N/A"}
File System:     {cert.FileSystem ?? "N/A"}
Volume Label:    {cert.VolumeLabel ?? "N/A"}

--------------------------------------------------------------------------------
                            RECOMMENDATION
--------------------------------------------------------------------------------
{(cert.Recommended ? "✓ This disk is RECOMMENDED for use." : "⚠ This disk has issues and is NOT recommended.")}
{cert.RecommendationNotes}

================================================================================
                    Generated by DiskChecker
                Professional Disk Testing Solution
================================================================================
";
    }

    private static string FormatCapacity(long bytes)
    {
        if (bytes <= 0) return "Unknown";
        var gb = bytes / (1024.0 * 1024.0 * 1024.0);
        if (gb >= 1000)
        {
            return $"{gb / 1024.0:F2} TB";
        }
        return $"{gb:F0} GB";
    }

    private static string GenerateRecommendation(TestSession session)
    {
        if (session.Result == TestResult.Pass && session.Score >= 90)
        {
            return "Excellent condition. Disk recommended for all purposes.";
        }
        if (session.Result == TestResult.Pass && session.Score >= 70)
        {
            return "Good condition. Disk suitable for general use.";
        }
        if (session.Result == TestResult.Warning)
        {
            return "Some issues detected. Use with caution. Not recommended for critical data.";
        }
        if (session.Result == TestResult.Fail)
        {
            return "Disk failed testing. Not recommended for use. Consider replacement.";
        }
        return "Condition uncertain. Additional testing recommended.";
    }

    private static bool IsCriticalAttribute(int attributeId)
    {
        var criticalIds = new[] { 5, 10, 177, 179, 181, 182, 187, 188, 190, 194, 195, 196, 197, 198, 199, 231, 233 };
        return Array.BinarySearch(criticalIds, attributeId) >= 0;
    }

    private static Color GetGradeColor(string grade)
    {
        return grade switch
        {
            "A" => Color.FromArgb(39, 174, 96),   // Green
            "B" => Color.FromArgb(46, 204, 113),  // Light green
            "C" => Color.FromArgb(241, 196, 15),  // Yellow
            "D" => Color.FromArgb(230, 126, 34), // Orange
            "E" => Color.FromArgb(231, 76, 60),   // Red-orange
            "F" => Color.FromArgb(192, 57, 43),   // Red
            _ => Color.Gray
        };
    }

    private static string TrimValue(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : $"{normalized[..(maxLength - 1)]}…";
    }
}