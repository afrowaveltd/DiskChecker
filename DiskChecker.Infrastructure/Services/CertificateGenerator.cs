using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// Cross-platform service for generating disk certificates and PDFs using SkiaSharp.
/// Works on Windows, Linux, and macOS without native dependencies.
/// </summary>
public class CertificateGenerator : ICertificateGenerator
{
    private readonly ILogger<CertificateGenerator>? _logger;
    private readonly ISettingsService? _settingsService;
    private readonly string _certificatesDirectory;
    private readonly string _labelsDirectory;
    private readonly string _chartCacheDirectory;

    // Chart rendering constants
    private const int MaxChartPoints = 512;
    private const int CertificateChartPoints = 32;

    // Certificate JPEG dimensions (A4 at 150 DPI)
    private const int CertWidth = 1240;
    private const int CertHeight = 1754;

    // Font family fallback chain (cross-platform)
    private const string FontSansSerif = "DejaVu Sans";
    private const string FontSansSerifFallback = "Noto Sans";
    private const string FontSansSerifSystem = "Arial";

    public CertificateGenerator(ILogger<CertificateGenerator>? logger = null, ISettingsService? settingsService = null)
    {
        _logger = logger;
        _settingsService = settingsService;

        var basePath = GetCertificateBasePath();
        _certificatesDirectory = Path.Combine(basePath, "Certificates");
        _labelsDirectory = Path.Combine(basePath, "Labels");
        _chartCacheDirectory = Path.Combine(basePath, "ChartCache");

        Directory.CreateDirectory(_certificatesDirectory);
        Directory.CreateDirectory(_labelsDirectory);
        Directory.CreateDirectory(_chartCacheDirectory);
    }

    private string GetCertificateBasePath()
    {
        if (_settingsService != null)
        {
            var customPath = _settingsService.GetCertificatePathAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(customPath) && Directory.Exists(customPath))
                return customPath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiskChecker");
    }

    // ──────────────────────────────────────────────
    //  Certificate generation
    // ──────────────────────────────────────────────

    public Task<DiskCertificate> GenerateCertificateAsync(TestSession session, DiskCard diskCard)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(diskCard);

        return Task.Run(() =>
        {
            var reallocatedSectors = session.SmartBefore?.ReallocatedSectorCount
                ?? GetSmartAttributeValue(session, 5) ?? 0;
            var pendingSectors = session.SmartBefore?.PendingSectorCount
                ?? GetSmartAttributeValue(session, 197) ?? 0;
            var powerOnHours = session.SmartBefore?.PowerOnHours ?? diskCard.PowerOnHours ?? 0;
            var powerCycles = session.SmartBefore?.PowerCycleCount ?? diskCard.PowerCycleCount ?? 0;

            var certificate = new DiskCertificate
            {
                DiskCardId = diskCard.Id,
                TestSessionId = session.Id,
                GeneratedAt = DateTime.UtcNow,
                GeneratedBy = Environment.UserName,
                DiskModel = diskCard.ModelName,
                SerialNumber = ResolveDisplaySerial(session.SmartBefore?.SerialNumber, diskCard.SerialNumber),
                Capacity = FormatCapacity(diskCard.Capacity),
                DiskType = diskCard.DiskType,
                Firmware = diskCard.FirmwareVersion,
                Interface = diskCard.InterfaceType,
                TestType = session.TestType.ToString(),
                TestDuration = session.Duration,
                ErrorCount = session.Errors.Count,
                AvgWriteSpeed = session.AverageWriteSpeedMBps,
                MaxWriteSpeed = session.MaxWriteSpeedMBps,
                AvgReadSpeed = session.AverageReadSpeedMBps,
                MaxReadSpeed = session.MaxReadSpeedMBps,
                TemperatureRange = session.StartTemperature.HasValue && session.MaxTemperature.HasValue
                    ? $"{session.StartTemperature.Value}°C - {session.MaxTemperature.Value}°C"
                    : "N/A",
                SmartPassed = session.SmartBefore?.IsHealthy ?? (reallocatedSectors == 0 && pendingSectors == 0),
                PowerOnHours = powerOnHours,
                PowerCycles = powerCycles,
                ReallocatedSectors = reallocatedSectors,
                PendingSectors = pendingSectors,
                SanitizationPerformed = session.TestType == TestType.Sanitization,
                SanitizationMethod = session.TestType == TestType.Sanitization ? "Zero-fill" : null,
                DataVerified = session.VerificationErrors == 0,
                PartitionScheme = session.PartitionScheme,
                FileSystem = session.FileSystem,
                VolumeLabel = session.VolumeLabel,
                Status = CertificateStatus.Active,
                Recommended = session.Result == TestResult.Pass
                    && !string.Equals(session.Grade, "E", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(session.Grade, "F", StringComparison.OrdinalIgnoreCase),
                RecommendationNotes = GenerateRecommendation(session),
                Notes = session.Notes,
                ChartImagePath = session.ChartImagePath
            };

            certificate.WriteProfilePoints = DownsampleSpeeds(session.WriteSamples.Select(s => s.SpeedMBps), CertificateChartPoints);
            certificate.ReadProfilePoints = DownsampleSpeeds(session.ReadSamples.Select(s => s.SpeedMBps), CertificateChartPoints);

            var calculated = CalculateGrade(session);
            var grade = session.SmartBefore != null ? calculated.grade
                : (string.IsNullOrWhiteSpace(session.Grade) ? calculated.grade : session.Grade);
            var score = session.SmartBefore != null ? calculated.score
                : (session.Score > 0 ? session.Score : calculated.score);
            certificate.Grade = grade;
            certificate.Score = score;
            certificate.HealthStatus = session.HealthAssessment.ToString();

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
        });
    }

    // ──────────────────────────────────────────────
    //  PDF generation (SkiaSharp → JPEG → PDF)
    // ──────────────────────────────────────────────

    public async Task<string> GeneratePdfAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (string.IsNullOrWhiteSpace(certificate.CertificateNumber))
            certificate.CertificateNumber = $"TMP-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var safeSerial = SanitizeFileName(certificate.SerialNumber ?? "NOSN");
        var datePart = certificate.GeneratedAt.ToString("yyyyMMdd");
        var baseName = $"{safeSerial}_{datePart}";
        var index = 1;
        string fileName;
        do
        {
            fileName = $"{baseName}_{index}.pdf";
            index++;
        } while (File.Exists(Path.Combine(_certificatesDirectory, fileName)));

        var filePath = Path.Combine(_certificatesDirectory, fileName);

        var jpegBytes = await RenderCertificateJpegAsync(certificate);
        var pdfBytes = BuildImagePdfDocument(jpegBytes, CertWidth, CertHeight);
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        if (_logger?.IsEnabled(LogLevel.Information) == true)
            _logger.LogInformation("Certificate PDF saved: {Path}", filePath);
        return filePath;
    }

    /// <summary>
    /// Renders the certificate as a JPEG using SkiaSharp (cross-platform).
    /// </summary>
    private static Task<byte[]> RenderCertificateJpegAsync(DiskCertificate cert)
    {
        ArgumentNullException.ThrowIfNull(cert);

        return Task.Run(() =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(CertWidth, CertHeight));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            // Resolve fonts with fallback chain
            using var titleFont = ResolveFont(34, bold: true);
            using var sectionFont = ResolveFont(16, bold: true);
            using var labelFont = ResolveFont(12, bold: true);
            using var valueFont = ResolveFont(12, bold: false);
            using var gradeFont = ResolveFont(120, bold: true);
            using var scoreFont = ResolveFont(22, bold: true);
            using var smallFont = ResolveFont(10, bold: false);

            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var mutedPaint = new SKPaint { Color = new SKColor(80, 80, 80), IsAntialias = true };
            using var accentPaint = new SKPaint { Color = new SKColor(15, 76, 129), IsAntialias = true };
            using var panelPaint = new SKPaint { Color = new SKColor(246, 248, 251), IsAntialias = true, Style = SKPaintStyle.Fill };
            using var borderPaint = new SKPaint { Color = new SKColor(210, 215, 223), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
            using var writePen = new SKPaint { Color = new SKColor(220, 38, 38), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round };
            using var readPen = new SKPaint { Color = new SKColor(5, 150, 105), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, StrokeCap = SKStrokeCap.Round };
            using var axisPen = new SKPaint { Color = new SKColor(203, 213, 225), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };

            var gradeColor = GetGradeColor(cert.Grade);
            using var gradePaint = new SKPaint { Color = gradeColor, IsAntialias = true };
            using var gradeStrokePaint = new SKPaint { Color = gradeColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 6f };

            // Border
            canvas.DrawRect(20, 20, CertWidth - 40, CertHeight - 40, borderPaint);

            // Title
            DrawText(canvas, "CERTIFIKÁT KVALITY DISKU", 48, 42, titleFont, accentPaint);
            DrawText(canvas, "DiskChecker – Profesionální diagnóza disků", 52, 98, smallFont, mutedPaint);

            // Info panel
            float y = 140;
            canvas.DrawRect(48, y, 760, 300, panelPaint);
            canvas.DrawRect(48, y, 760, 300, borderPaint);

            void DrawLine(string label, string value, int row)
            {
                float yy = y + 18 + row * 34;
                DrawText(canvas, label, 64, yy, labelFont, textPaint);
                DrawText(canvas, value, 250, yy, valueFont, textPaint);
            }

            DrawLine("Model:", cert.DiskModel, 0);
            DrawLine("Sériové číslo:", cert.SerialNumber, 1);
            DrawLine("Kapacita:", cert.Capacity, 2);
            DrawLine("Typ disku:", cert.DiskType, 3);
            DrawLine("Provozní hodiny:", cert.PowerOnHours > 0 ? cert.PowerOnHours.ToString("#,0", CultureInfo.InvariantCulture) : "N/A", 4);
            DrawLine("Počet startů:", cert.PowerCycles > 0 ? cert.PowerCycles.ToString("#,0", CultureInfo.InvariantCulture) : "N/A", 5);
            DrawLine("Číslo certifikátu:", cert.CertificateNumber, 6);
            DrawLine("Vygenerováno:", cert.GeneratedAt.ToString("dd.MM.yyyy HH:mm"), 7);

            // Grade seal
            canvas.DrawRect(838, y, 350, 300, panelPaint);
            canvas.DrawRect(838, y, 350, 300, borderPaint);
            DrawText(canvas, "KONEČNÁ ZNÁMKA", 926, y + 18, labelFont, textPaint);

            const float sealSize = 150f;
            float sealX = 943f;
            float sealY = y + 78f;
            canvas.DrawOval(sealX, sealY, sealSize, sealSize, gradeStrokePaint);

            var gradeText = cert.Grade;
            float gradeTextWidth = gradeFont.MeasureText(gradeText);
            float gradeX = sealX + (sealSize - gradeTextWidth) / 2f;
            // Center vertically: DrawText adds font.Size, so we subtract it here
            // to place baseline correctly for visual centering
            float gradeY = sealY + (sealSize - gradeFont.Size) / 2f;
            DrawText(canvas, gradeText, gradeX, gradeY, gradeFont, gradePaint);

            var scoreText = $"Skóre: {cert.Score:F0}/100";
            float scoreWidth = scoreFont.MeasureText(scoreText);
            DrawText(canvas, scoreText, sealX + (sealSize - scoreWidth) / 2f, sealY + sealSize + 8f, scoreFont, textPaint);

            // Test results
            y += 330;
            DrawText(canvas, "Výsledky testu", 48, y, sectionFont, accentPaint);
            y += 34;
            canvas.DrawRect(48, y, CertWidth - 96, 170, panelPaint);
            canvas.DrawRect(48, y, CertWidth - 96, 170, borderPaint);
            DrawText(canvas, $"Typ: {cert.TestType}", 64, y + 16, valueFont, textPaint);
            DrawText(canvas, $"Doba: {cert.TestDuration:hh\\:mm\\:ss}", 64, y + 46, valueFont, textPaint);
            DrawText(canvas, $"Chyby: {cert.ErrorCount}", 64, y + 76, valueFont, textPaint);
            DrawText(canvas, $"Teplota: {cert.TemperatureRange}", 64, y + 106, valueFont, textPaint);
            DrawText(canvas, $"Průměrný zápis: {cert.AvgWriteSpeed:F1} MB/s", 520, y + 16, valueFont, textPaint);
            DrawText(canvas, $"Průměrné čtení: {cert.AvgReadSpeed:F1} MB/s", 520, y + 46, valueFont, textPaint);
            DrawText(canvas, $"Stav: {cert.HealthStatus}", 520, y + 76, valueFont, textPaint);

            // SMART summary
            y += 190;
            DrawText(canvas, "SMART souhrn", 48, y, sectionFont, accentPaint);
            y += 34;
            canvas.DrawRect(48, y, CertWidth - 96, 190, panelPaint);
            canvas.DrawRect(48, y, CertWidth - 96, 190, borderPaint);
            DrawText(canvas, $"Provozní hodiny: {(cert.PowerOnHours > 0 ? cert.PowerOnHours.ToString("#,0", CultureInfo.InvariantCulture) : "N/A")}", 64, y + 16, valueFont, textPaint);
            DrawText(canvas, $"Počet startů: {(cert.PowerCycles > 0 ? cert.PowerCycles.ToString("#,0", CultureInfo.InvariantCulture) : "N/A")}", 64, y + 44, valueFont, textPaint);
            DrawText(canvas, $"Realokované sektory: {cert.ReallocatedSectors}", 520, y + 16, valueFont, textPaint);
            DrawText(canvas, $"Čekající sektory: {cert.PendingSectors}", 520, y + 44, valueFont, textPaint);
            DrawText(canvas, "Legenda známek:", 64, y + 82, valueFont, accentPaint);
            DrawText(canvas, "A = výborný stav | B = velmi dobrý stav", 64, y + 110, smallFont, textPaint);
            DrawText(canvas, "C = dobrý stav | D = opotřebený disk", 64, y + 132, smallFont, textPaint);
            DrawText(canvas, "E = rizikový disk | F = kritický / vadný", 64, y + 154, smallFont, textPaint);

            // Performance chart
            y += 220;
            DrawText(canvas, "Výkonový profil testu", 48, y, sectionFont, accentPaint);
            y += 34;

            float chartX = 72f;
            float chartY = y;
            float chartW = CertWidth - 160f;
            float chartH = 260f;
            canvas.DrawRect(chartX, chartY, chartW, chartH, borderPaint);

            if (!string.IsNullOrWhiteSpace(cert.ChartImagePath) && File.Exists(cert.ChartImagePath))
            {
                using var chartImage = SKBitmap.Decode(cert.ChartImagePath);
                if (chartImage != null)
                    canvas.DrawBitmap(chartImage, new SKRect(chartX, chartY, chartX + chartW, chartY + chartH));
            }
            else
            {
                // Draw axes
                canvas.DrawLine(chartX + 30, chartY + 14, chartX + 30, chartY + chartH - 26, axisPen);
                canvas.DrawLine(chartX + 30, chartY + chartH - 26, chartX + chartW - 20, chartY + chartH - 26, axisPen);
                // Grid lines
                for (int g = 1; g <= 3; g++)
                {
                    float gx = chartX + 30 + (chartW - 50) * g / 4f;
                    canvas.DrawLine(gx, chartY + 14, gx, chartY + chartH - 26, axisPen);
                }

                var (writePoints, readPoints) = GetProfilePointsForChart(cert);
                var maxSpeed = Math.Max(writePoints.Count > 0 ? writePoints.Max() : 0, readPoints.Count > 0 ? readPoints.Max() : 0);
                if (maxSpeed <= 0) maxSpeed = 1;

                DrawProfilePolyline(canvas, writePen, writePoints, maxSpeed, chartX + 30, chartY + 14, chartW - 50, chartH - 40);
                DrawProfilePolyline(canvas, readPen, readPoints, maxSpeed, chartX + 30, chartY + 14, chartW - 50, chartH - 40);

                DrawText(canvas, "MB/s", chartX - 4, chartY + 8, smallFont, mutedPaint);
                DrawText(canvas, $"{maxSpeed:F0}", chartX - 22, chartY + 14, smallFont, mutedPaint);
                DrawText(canvas, $"{maxSpeed / 2:F0}", chartX - 22, chartY + (chartH - 40) / 2 + 10, smallFont, mutedPaint);
                DrawText(canvas, "0", chartX - 12, chartY + chartH - 30, smallFont, mutedPaint);
                DrawText(canvas, "0 %", chartX + 26, chartY + chartH - 18, smallFont, mutedPaint);
                DrawText(canvas, "25 %", chartX + (chartW * 0.25f) - 8, chartY + chartH - 18, smallFont, mutedPaint);
                DrawText(canvas, "50 %", chartX + chartW / 2 - 8, chartY + chartH - 18, smallFont, mutedPaint);
                DrawText(canvas, "75 %", chartX + (chartW * 0.75f) - 8, chartY + chartH - 18, smallFont, mutedPaint);
                DrawText(canvas, "100 %", chartX + chartW - 40, chartY + chartH - 18, smallFont, mutedPaint);

                using var legendWritePaint = new SKPaint { Color = new SKColor(220, 38, 38), IsAntialias = true };
                using var legendReadPaint = new SKPaint { Color = new SKColor(5, 150, 105), IsAntialias = true };
                DrawText(canvas, "Zápis", chartX + chartW - 150, chartY + 8, smallFont, legendWritePaint);
                DrawText(canvas, "Čtení", chartX + chartW - 90, chartY + 8, smallFont, legendReadPaint);
            }

            // Recommendation
            y += 290;
            DrawText(canvas, "Doporučení", 48, y, sectionFont, accentPaint);
            y += 34;
            canvas.DrawRect(48, y, CertWidth - 96, 140, panelPaint);
            canvas.DrawRect(48, y, CertWidth - 96, 140, borderPaint);
            DrawTextWrapped(canvas, cert.RecommendationNotes ?? "Není k dispozici", 64, y + 16, CertWidth - 130, 100, valueFont, textPaint);

            // Diagnostic notes
            if (!string.IsNullOrWhiteSpace(cert.Notes))
            {
                y += 160;
                DrawText(canvas, "Důvody hodnocení", 48, y, sectionFont, accentPaint);
                y += 34;
                float notesHeight = Math.Max(60, valueFont.MeasureText(cert.Notes) / (CertWidth - 130) * valueFont.Size * 1.5f + 20);
                canvas.DrawRect(48, y, CertWidth - 96, notesHeight, panelPaint);
                canvas.DrawRect(48, y, CertWidth - 96, notesHeight, borderPaint);
                DrawTextWrapped(canvas, cert.Notes, 64, y + 16, CertWidth - 130, notesHeight - 20, valueFont, textPaint);
            }

            // Encode as JPEG
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Resolves a font with cross-platform fallback chain.
    /// Tries DejaVu Sans → Noto Sans → Arial → system default.
    /// </summary>
    private static SKFont ResolveFont(float size, bool bold)
    {
        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;

        // Try common Linux fonts first, then Windows/macOS
        string[] families = { FontSansSerif, FontSansSerifFallback, FontSansSerifSystem, "Helvetica", "sans-serif" };

        foreach (var family in families)
        {
            var typeface = SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            if (typeface != null && typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                return new SKFont(typeface, size);
            }
            typeface?.Dispose();
        }

        // Ultimate fallback: system default
        return new SKFont(SKTypeface.Default, size);
    }

    private static void DrawText(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint)
    {
        canvas.DrawText(text, x, y + font.Size, font, paint);
    }

    private static void DrawTextWrapped(SKCanvas canvas, string text, float x, float y, float maxWidth, float maxHeight, SKFont font, SKPaint paint)
    {
        // Simple word-wrap using SkiaSharp text measurement
        var words = text.Split(' ');
        var lines = new List<string>();
        var currentLine = "";
        float lineHeight = font.Size * 1.4f;

        foreach (var word in words)
        {
            var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            if (font.MeasureText(testLine) > maxWidth && !string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }
        if (!string.IsNullOrEmpty(currentLine))
            lines.Add(currentLine);

        float cy = y;
        foreach (var line in lines)
        {
            if (cy + lineHeight > y + maxHeight) break;
            DrawText(canvas, line, x, cy, font, paint);
            cy += lineHeight;
        }
    }

    // ──────────────────────────────────────────────
    //  PDF document builder (lightweight, no deps)
    // ──────────────────────────────────────────────

    private static byte[] BuildImagePdfDocument(byte[] jpegBytes, int imageWidth, int imageHeight)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        var offsets = new List<long> { 0 };

        void WriteObject(int objectNumber, string body)
        {
            writer.Flush();
            offsets.Add(ms.Position);
            writer.Write($"{objectNumber} 0 obj\n");
            writer.Write(body);
            writer.Write("\nendobj\n");
        }

        writer.Write("%PDF-1.4\n");

        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im1 5 0 R >> >> /Contents 4 0 R >>");

        var content = "q\n595 0 0 842 0 0 cm\n/Im1 Do\nQ\n";
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(content);
        writer.Flush();
        offsets.Add(ms.Position);
        writer.Write("4 0 obj\n");
        writer.Write($"<< /Length {contentBytes.Length} >>\nstream\n");
        writer.Flush();
        ms.Write(contentBytes, 0, contentBytes.Length);
        writer.Write("endstream\nendobj\n");

        writer.Flush();
        offsets.Add(ms.Position);
        writer.Write("5 0 obj\n");
        writer.Write($"<< /Type /XObject /Subtype /Image /Width {imageWidth} /Height {imageHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
        writer.Flush();
        ms.Write(jpegBytes, 0, jpegBytes.Length);
        writer.Write("\nendstream\nendobj\n");

        writer.Flush();
        var xrefStart = ms.Position;
        writer.Write($"xref\n0 {offsets.Count}\n");
        writer.Write("0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
            writer.Write($"{offsets[i]:D10} 00000 n \n");

        writer.Write("trailer\n");
        writer.Write($"<< /Size {offsets.Count} /Root 1 0 R >>\n");
        writer.Write("startxref\n");
        writer.Write($"{xrefStart}\n");
        writer.Write("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    // ──────────────────────────────────────────────
    //  Chart helpers
    // ──────────────────────────────────────────────

    private static List<double> DownsampleSpeeds(IEnumerable<double> speeds, int targetPoints)
    {
        var values = speeds.Where(v => v > 0).ToList();
        if (values.Count == 0) return new List<double>();
        if (values.Count <= targetPoints) return values;

        var result = new List<double>(targetPoints);
        var bucketSize = values.Count / (double)targetPoints;

        for (var i = 0; i < targetPoints; i++)
        {
            var start = (int)Math.Floor(i * bucketSize);
            var end = (int)Math.Floor((i + 1) * bucketSize);
            end = Math.Clamp(end, start + 1, values.Count);

            var sum = 0d;
            var count = 0;
            for (var j = start; j < end; j++) { sum += values[j]; count++; }
            result.Add(count > 0 ? sum / count : values[Math.Min(start, values.Count - 1)]);
        }
        return result;
    }

    private static (List<double> Write, List<double> Read) GetProfilePointsForChart(DiskCertificate cert)
    {
        var write = cert.WriteProfilePoints?.ToList() ?? new List<double>();
        var read = cert.ReadProfilePoints?.ToList() ?? new List<double>();
        return (write, read);
    }

    private static void DrawProfilePolyline(
        SKCanvas canvas, SKPaint pen, List<double> values, double maxValue,
        float chartX, float chartY, float chartW, float chartH)
    {
        if (values.Count < 2) return;

        try
        {
            using var path = new SKPath();
            float stepX = chartW / (values.Count - 1);

            for (int i = 0; i < values.Count; i++)
            {
                float x = chartX + i * stepX;
                float y = chartY + chartH - (float)(values[i] / maxValue * chartH);
                if (i == 0) path.MoveTo(x, y);
                else path.LineTo(x, y);
            }

            canvas.DrawPath(path, pen);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DrawProfilePolyline error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────
    //  Preview, label, and chart image generation
    // ──────────────────────────────────────────────

    public Task<byte[]> GeneratePreviewAsync(DiskCertificate certificate)
    {
        return RenderCertificateJpegAsync(certificate);
    }

    public (string grade, double score) CalculateGrade(TestSession session)
    {
        double score = 100;

        if (session.SmartBefore != null)
        {
            if (session.SmartBefore.ReallocatedSectorCount > 0)
                score -= (double)(session.SmartBefore.ReallocatedSectorCount.Value * 3);
            if (session.SmartBefore.PendingSectorCount > 0)
                score -= (double)(session.SmartBefore.PendingSectorCount.Value * 5);
            if (session.SmartBefore.UncorrectableErrorCount > 0)
                score -= (double)(session.SmartBefore.UncorrectableErrorCount.Value * 10);
        }

        score -= session.Errors.Count * 2;

        if (session.AverageWriteSpeedMBps > 0 && session.MaxWriteSpeedMBps > 0)
        {
            var consistency = session.AverageWriteSpeedMBps / session.MaxWriteSpeedMBps;
            if (consistency < 0.7) score -= (0.7 - consistency) * 50;
        }

        score = Math.Clamp(score, 0, 100);

        var grade = score switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 55 => "D",
            >= 40 => "E",
            _ => "F"
        };

        return (grade, score);
    }

    // ──────────────────────────────────────────────
    //  Utility methods
    // ──────────────────────────────────────────────

    private static string FormatCapacity(long bytes)
    {
        if (bytes >= 1_000_000_000_000L) return $"{bytes / 1_000_000_000_000.0:F2} TB";
        if (bytes >= 1_000_000_000L) return $"{bytes / 1_000_000_000.0:F2} GB";
        if (bytes >= 1_000_000L) return $"{bytes / 1_000_000.0:F2} MB";
        return $"{bytes} B";
    }

    private static string GenerateRecommendation(TestSession session)
    {
        if (session.Result == TestResult.Pass
            && !string.Equals(session.Grade, "E", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(session.Grade, "F", StringComparison.OrdinalIgnoreCase))
            return "Disk je v dobrém stavu a lze jej bezpečně používat.";

        if (session.SmartBefore?.ReallocatedSectorCount > 0 || session.SmartBefore?.PendingSectorCount > 0)
            return "Disk vykazuje známky opotřebení. Doporučujeme pravidelnou kontrolu SMART a zálohování důležitých dat.";

        if (session.Errors.Count > 0)
            return "Během testu byly detekovány chyby. Zvažte výměnu disku, zejména pokud se chyby opakují.";

        return "Disk vyžaduje pozornost. Doporučujeme další diagnostiku nebo výměnu.";
    }

    private static string ResolveDisplaySerial(string? smartSerial, string? storedSerial)
    {
        if (!string.IsNullOrWhiteSpace(smartSerial)) return smartSerial;
        if (!string.IsNullOrWhiteSpace(storedSerial)) return storedSerial;
        return "-";
    }

    private static bool IsCriticalAttribute(int attributeId)
    {
        return attributeId is 5 or 196 or 197 or 198 or 10 or 187 or 188 or 1 or 7 or 9 or 194;
    }

    private static SKColor GetGradeColor(string grade)
    {
        return grade?.ToUpperInvariant() switch
        {
            "A" => new SKColor(5, 150, 105),
            "B" => new SKColor(59, 130, 246),
            "C" => new SKColor(245, 158, 11),
            "D" => new SKColor(249, 115, 22),
            "E" => new SKColor(220, 38, 38),
            "F" => new SKColor(185, 28, 28),
            _ => new SKColor(100, 100, 100)
        };
    }

    private static string TrimValue(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }

    private static int? GetSmartAttributeValue(TestSession session, int attributeId)
    {
        var raw = session.SmartBefore?.Attributes?.FirstOrDefault(a => a.Id == attributeId)?.RawValue;
        return raw.HasValue ? (int?)raw.Value : null;
    }

    public Task<string?> GenerateAndStoreChartImageAsync(TestSession session, CancellationToken cancellationToken = default)
    {
        if (session.WriteSamples.Count == 0 && session.ReadSamples.Count == 0)
            return Task.FromResult<string?>(null);

        return Task.Run(() =>
        {
            try
            {
                var fileName = $"chart_{session.SessionId:N}.png";
                var filePath = Path.Combine(_chartCacheDirectory, fileName);

                RenderChartImage(session, filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to generate chart image for session {SessionId}", session.SessionId);
                return null;
            }
        }, cancellationToken);
    }

    private static void RenderChartImage(TestSession session, string filePath)
    {
        const int width = 800;
        const int height = 400;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var bgPaint = new SKPaint { Color = new SKColor(248, 250, 252), Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, 0, width, height, bgPaint);

        using var axisPaint = new SKPaint { Color = new SKColor(203, 213, 225), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
        using var writePaint = new SKPaint { Color = new SKColor(220, 38, 38), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, StrokeCap = SKStrokeCap.Round };
        using var readPaint = new SKPaint { Color = new SKColor(5, 150, 105), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, StrokeCap = SKStrokeCap.Round };
        using var font = ResolveFont(10, bold: false);
        using var textPaint = new SKPaint { Color = new SKColor(100, 100, 100), IsAntialias = true };

        float margin = 40;
        float chartW = width - 2 * margin;
        float chartH = height - 2 * margin;

        // Axes
        canvas.DrawLine(margin, margin, margin, height - margin, axisPaint);
        canvas.DrawLine(margin, height - margin, width - margin, height - margin, axisPaint);

        var writeSamples = session.WriteSamples.Where(s => s.SpeedMBps > 0).ToList();
        var readSamples = session.ReadSamples.Where(s => s.SpeedMBps > 0).ToList();
        var maxSpeed = Math.Max(
            writeSamples.Count > 0 ? writeSamples.Max(s => s.SpeedMBps) : 0,
            readSamples.Count > 0 ? readSamples.Max(s => s.SpeedMBps) : 0);
        if (maxSpeed <= 0) maxSpeed = 1;

        void DrawLine(List<SpeedSample> samples, SKPaint paint)
        {
            if (samples.Count < 2) return;
            using var path = new SKPath();
            for (int i = 0; i < samples.Count; i++)
            {
                float x = margin + (float)i / (samples.Count - 1) * chartW;
                float y = height - margin - (float)(samples[i].SpeedMBps / maxSpeed * chartH);
                if (i == 0) path.MoveTo(x, y);
                else path.LineTo(x, y);
            }
            canvas.DrawPath(path, paint);
        }

        DrawLine(writeSamples, writePaint);
        DrawLine(readSamples, readPaint);

        // Labels
        DrawText(canvas, $"{maxSpeed:F0} MB/s", margin - 30, margin, font, textPaint);
        DrawText(canvas, "0", margin - 10, height - margin, font, textPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.OpenWrite(filePath);
        data.SaveTo(fs);
    }

    public Task<string?> EnsureChartImageAsync(TestSession session, CancellationToken cancellationToken = default)
    {
        // Check if chart image already exists
        if (!string.IsNullOrWhiteSpace(session.ChartImagePath) && File.Exists(session.ChartImagePath))
            return Task.FromResult<string?>(session.ChartImagePath);

        // Generate new chart image
        return GenerateAndStoreChartImageAsync(session, cancellationToken);
    }

    public Task<string> GenerateLabelAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(certificate.CertificateNumber))
                certificate.CertificateNumber = $"TMP-{DateTime.UtcNow:yyyyMMddHHmmss}";

            var safeSerial = SanitizeFileName(certificate.SerialNumber ?? "NOSN");
            var datePart = certificate.GeneratedAt.ToString("yyyyMMdd");
            var fileName = $"{safeSerial}_{datePart}_label.png";
            var filePath = Path.Combine(_labelsDirectory, fileName);

            const int labelWidth = 600;
            const int labelHeight = 300;

            using var surface = SKSurface.Create(new SKImageInfo(labelWidth, labelHeight));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            using var titleFont = ResolveFont(22, bold: true);
            using var valueFont = ResolveFont(14, bold: false);
            using var gradeFont = ResolveFont(64, bold: true);
            using var smallFont = ResolveFont(10, bold: false);

            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var accentPaint = new SKPaint { Color = new SKColor(15, 76, 129), IsAntialias = true };
            using var borderPaint = new SKPaint { Color = new SKColor(210, 215, 223), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };

            var gradeColor = GetGradeColor(certificate.Grade);
            using var gradePaint = new SKPaint { Color = gradeColor, IsAntialias = true };
            using var gradeStrokePaint = new SKPaint { Color = gradeColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };

            // Border
            canvas.DrawRect(10, 10, labelWidth - 20, labelHeight - 20, borderPaint);

            // Title
            DrawText(canvas, "CERTIFIKÁT KVALITY DISKU", 20, 20, titleFont, accentPaint);

            // Info
            DrawText(canvas, $"Model: {certificate.DiskModel}", 20, 60, valueFont, textPaint);
            DrawText(canvas, $"S/N: {certificate.SerialNumber}", 20, 84, valueFont, textPaint);
            DrawText(canvas, $"Kapacita: {certificate.Capacity}", 20, 108, valueFont, textPaint);
            DrawText(canvas, $"Známka: {certificate.Grade}  |  Skóre: {certificate.Score:F0}/100", 20, 132, valueFont, textPaint);
            DrawText(canvas, $"Datum: {certificate.GeneratedAt:dd.MM.yyyy HH:mm}", 20, 156, valueFont, textPaint);

            // Grade seal
            float sealX = labelWidth - 160;
            float sealY = 40;
            float sealSize = 100f;
            canvas.DrawOval(sealX, sealY, sealSize, sealSize, gradeStrokePaint);

            var gradeText = certificate.Grade;
            float gradeTextWidth = gradeFont.MeasureText(gradeText);
            float gradeX = sealX + (sealSize - gradeTextWidth) / 2f;
            // Center vertically: DrawText adds font.Size, so we subtract it here
            float gradeY = sealY + (sealSize - gradeFont.Size) / 2f;
            DrawText(canvas, gradeText, gradeX, gradeY, gradeFont, gradePaint);

            // Footer
            DrawText(canvas, $"DiskChecker v1.0 | {certificate.CertificateNumber}", 20, labelHeight - 30, smallFont, textPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            using var fs = File.OpenWrite(filePath);
            data.SaveTo(fs);

            if (_logger?.IsEnabled(LogLevel.Information) == true)
                _logger.LogInformation("Certificate label saved: {Path}", filePath);
            return filePath;
        });
    }

    private static List<double> DownsampleTemperatures(IReadOnlyList<TemperatureSample> samples, int targetPoints)
    {
        if (samples.Count <= targetPoints)
            return samples.Select(s => (double)s.TemperatureCelsius).ToList();

        var result = new List<double>(targetPoints);
        var bucketSize = samples.Count / (double)targetPoints;

        for (var i = 0; i < targetPoints; i++)
        {
            var start = (int)Math.Floor(i * bucketSize);
            var end = (int)Math.Floor((i + 1) * bucketSize);
            end = Math.Clamp(end, start + 1, samples.Count);

            var sum = 0;
            var count = 0;
            for (var j = start; j < end; j++) { sum += samples[j].TemperatureCelsius; count++; }
            result.Add(count > 0 ? sum / (double)count : samples[Math.Min(start, samples.Count - 1)].TemperatureCelsius);
        }
        return result;
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "NOSN";

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (Array.IndexOf(invalid, c) >= 0)
                sanitized.Append('_');
            else
                sanitized.Append(c);
        }

        var result = sanitized.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "NOSN" : result;
    }
}
