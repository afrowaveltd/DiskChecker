using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
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
    private readonly string _chartCacheDirectory;

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
        _chartCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiskChecker",
            "ChartCache");
        
        Directory.CreateDirectory(_certificatesDirectory);
        Directory.CreateDirectory(_labelsDirectory);
        Directory.CreateDirectory(_chartCacheDirectory);
    }

    public Task<DiskCertificate> GenerateCertificateAsync(TestSession session, DiskCard diskCard)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(diskCard);

        return Task.Run(() =>
        {
            var reallocatedSectors = session.SmartBefore?.ReallocatedSectorCount
                ?? GetSmartAttributeValue(session, 5)
                ?? 0;
            var pendingSectors = session.SmartBefore?.PendingSectorCount
                ?? GetSmartAttributeValue(session, 197)
                ?? 0;
            var powerOnHours = session.SmartBefore?.PowerOnHours
                ?? diskCard.PowerOnHours
                ?? 0;
            var powerCycles = session.SmartBefore?.PowerCycleCount
                ?? diskCard.PowerCycleCount
                ?? 0;

            var certificate = new DiskCertificate
            {
                DiskCardId = diskCard.Id,
                TestSessionId = session.Id,
                GeneratedAt = DateTime.UtcNow,
                GeneratedBy = Environment.UserName,

                // Disk information - use real serial number from SMART data if available
                DiskModel = diskCard.ModelName,
                SerialNumber = ResolveDisplaySerial(session.SmartBefore?.SerialNumber, diskCard.SerialNumber),
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
                SmartPassed = session.SmartBefore?.IsHealthy ?? (reallocatedSectors == 0 && pendingSectors == 0),
                PowerOnHours = powerOnHours,
                PowerCycles = powerCycles,
                ReallocatedSectors = reallocatedSectors,
                PendingSectors = pendingSectors,

                // Certificate status
                SanitizationPerformed = session.TestType == TestType.Sanitization,
                SanitizationMethod = session.TestType == TestType.Sanitization ? "Zero-fill" : null,
                DataVerified = session.VerificationErrors == 0,
                PartitionScheme = session.PartitionScheme,
                FileSystem = session.FileSystem,
                VolumeLabel = session.VolumeLabel,
                Status = CertificateStatus.Active,

                // Recommendation
                Recommended = session.Result == TestResult.Pass && !string.Equals(session.Grade, "E", StringComparison.OrdinalIgnoreCase) && !string.Equals(session.Grade, "F", StringComparison.OrdinalIgnoreCase),
                RecommendationNotes = GenerateRecommendation(session),
                Notes = session.Notes,
                ChartImagePath = session.ChartImagePath
            };

            certificate.WriteProfilePoints = DownsampleSpeeds(session.WriteSamples.Select(s => s.SpeedMBps), 32);
            certificate.ReadProfilePoints = DownsampleSpeeds(session.ReadSamples.Select(s => s.SpeedMBps), 32);

            // Calculate grade and score using shared method
            var grade = string.IsNullOrWhiteSpace(session.Grade) ? CalculateGrade(session).grade : session.Grade;
            var score = session.Score > 0 ? session.Score : CalculateGrade(session).score;
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
        });
    }

    public async Task<string> GeneratePdfAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (string.IsNullOrWhiteSpace(certificate.CertificateNumber))
        {
            certificate.CertificateNumber = $"TMP-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        var fileName = $"Certificate_{certificate.CertificateNumber}.pdf";
        var filePath = Path.Combine(_certificatesDirectory, fileName);

        var jpegBytes = await RenderCertificateJpegAsync(certificate);
        var pdfBytes = BuildImagePdfDocument(jpegBytes, 1240, 1754);
        await File.WriteAllBytesAsync(filePath, pdfBytes);

        certificate.PdfGenerated = true;
        certificate.PdfPath = filePath;

        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Certificate PDF generated: {FilePath}", filePath);
        }

        return filePath;
    }

    private static Task<byte[]> RenderCertificateJpegAsync(DiskCertificate cert)
    {
        ArgumentNullException.ThrowIfNull(cert);

        return Task.Run(() =>
        {
            const int width = 1240;
            const int height = 1754;

            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using var titleFont = new Font("Segoe UI", 34, FontStyle.Bold);
            using var sectionFont = new Font("Segoe UI", 16, FontStyle.Bold);
            using var labelFont = new Font("Segoe UI", 12, FontStyle.Bold);
            using var valueFont = new Font("Segoe UI", 12, FontStyle.Regular);
            using var gradeFont = new Font("Segoe UI", 120, FontStyle.Bold);
            using var scoreFont = new Font("Segoe UI", 22, FontStyle.Bold);
            using var smallFont = new Font("Segoe UI", 10, FontStyle.Regular);

            using var textBrush = new SolidBrush(Color.Black);
            using var mutedBrush = new SolidBrush(Color.FromArgb(80, 80, 80));
            using var accentBrush = new SolidBrush(Color.FromArgb(15, 76, 129));
            using var panelBrush = new SolidBrush(Color.FromArgb(246, 248, 251));
            using var borderPen = new Pen(Color.FromArgb(210, 215, 223), 2f);
            using var writePen = new Pen(Color.FromArgb(220, 38, 38), 3f);
            using var readPen = new Pen(Color.FromArgb(5, 150, 105), 3f);
            using var axisPen = new Pen(Color.FromArgb(203, 213, 225), 1f);
            using var gradeBrush = new SolidBrush(GetGradeColor(cert.Grade));

            graphics.DrawRectangle(borderPen, 20, 20, width - 40, height - 40);
            graphics.DrawString("CERTIFIKÁT KVALITY DISKU", titleFont, accentBrush, 48, 42);
            graphics.DrawString("DiskChecker – Profesionální diagnóza disků", smallFont, mutedBrush, 52, 98);

            var y = 140;
            graphics.FillRectangle(panelBrush, 48, y, 760, 300);
            graphics.DrawRectangle(borderPen, 48, y, 760, 300);

            void DrawLine(string label, string value, int row)
            {
                var yy = y + 18 + row * 34;
                graphics.DrawString(label, labelFont, textBrush, 64, yy);
                graphics.DrawString(value, valueFont, textBrush, 250, yy);
            }

            DrawLine("Model:", cert.DiskModel, 0);
            DrawLine("Sériové číslo:", cert.SerialNumber, 1);
            DrawLine("Kapacita:", cert.Capacity, 2);
            DrawLine("Typ disku:", cert.DiskType, 3);
            DrawLine("Provozní hodiny:", cert.PowerOnHours > 0 ? cert.PowerOnHours.ToString("#,0", CultureInfo.InvariantCulture) : "N/A", 4);
            DrawLine("Počet startů:", cert.PowerCycles > 0 ? cert.PowerCycles.ToString("#,0", CultureInfo.InvariantCulture) : "N/A", 5);
            DrawLine("Číslo certifikátu:", cert.CertificateNumber, 6);
            DrawLine("Vygenerováno:", cert.GeneratedAt.ToString("dd.MM.yyyy HH:mm"), 7);

            graphics.FillRectangle(panelBrush, 838, y, 350, 300);
            graphics.DrawRectangle(borderPen, 838, y, 350, 300);
            graphics.DrawString("KONEČNÁ ZNÁMKA", labelFont, textBrush, 926, y + 18);

            const float sealSize = 150f;
            var sealX = 943f;
            var sealY = y + 78f;
            using var sealPen = new Pen(GetGradeColor(cert.Grade), 6f);
            graphics.DrawEllipse(sealPen, sealX, sealY, sealSize, sealSize);

            var gradeText = cert.Grade;
            var gradeSize = graphics.MeasureString(gradeText, gradeFont);
            var gradeX = sealX + ((sealSize - gradeSize.Width) / 2f);
            var gradeY = sealY + ((sealSize - gradeSize.Height) / 2f) - 8f;
            graphics.DrawString(gradeText, gradeFont, gradeBrush, gradeX, gradeY);

            var scoreText = $"Skóre: {cert.Score:F0}/100";
            var scoreSize = graphics.MeasureString(scoreText, scoreFont);
            graphics.DrawString(scoreText, scoreFont, textBrush, sealX + ((sealSize - scoreSize.Width) / 2f), sealY + sealSize + 8f);

            y += 330;
            graphics.DrawString("Výsledky testu", sectionFont, accentBrush, 48, y);
            y += 34;
            graphics.FillRectangle(panelBrush, 48, y, width - 96, 170);
            graphics.DrawRectangle(borderPen, 48, y, width - 96, 170);
            graphics.DrawString($"Typ: {cert.TestType}", valueFont, textBrush, 64, y + 16);
            graphics.DrawString($"Doba: {cert.TestDuration:hh\\:mm\\:ss}", valueFont, textBrush, 64, y + 46);
            graphics.DrawString($"Chyby: {cert.ErrorCount}", valueFont, textBrush, 64, y + 76);
            graphics.DrawString($"Teplota: {cert.TemperatureRange}", valueFont, textBrush, 64, y + 106);
            graphics.DrawString($"Průměrný zápis: {cert.AvgWriteSpeed:F1} MB/s", valueFont, textBrush, 520, y + 16);
            graphics.DrawString($"Průměrné čtení: {cert.AvgReadSpeed:F1} MB/s", valueFont, textBrush, 520, y + 46);
            graphics.DrawString($"Stav: {cert.HealthStatus}", valueFont, textBrush, 520, y + 76);

            y += 190;
            graphics.DrawString("SMART souhrn", sectionFont, accentBrush, 48, y);
            y += 34;
            graphics.FillRectangle(panelBrush, 48, y, width - 96, 120);
            graphics.DrawRectangle(borderPen, 48, y, width - 96, 120);
            graphics.DrawString($"Provozní hodiny: {(cert.PowerOnHours > 0 ? cert.PowerOnHours.ToString("#,0", CultureInfo.InvariantCulture) : "N/A")}", valueFont, textBrush, 64, y + 16);
            graphics.DrawString($"Počet startů: {(cert.PowerCycles > 0 ? cert.PowerCycles.ToString("#,0", CultureInfo.InvariantCulture) : "N/A")}", valueFont, textBrush, 64, y + 44);
            graphics.DrawString($"Realokované sektory: {cert.ReallocatedSectors}", valueFont, textBrush, 520, y + 16);
            graphics.DrawString($"Čekající sektory: {cert.PendingSectors}", valueFont, textBrush, 520, y + 44);

            y += 150;
            graphics.DrawString("Výkonový profil testu", sectionFont, accentBrush, 48, y);
            y += 34;

            var chartX = 72f;
            var chartY = y;
            var chartW = width - 160f;
            var chartH = 260f;
            graphics.DrawRectangle(borderPen, chartX, chartY, chartW, chartH);

            if (!string.IsNullOrWhiteSpace(cert.ChartImagePath) && File.Exists(cert.ChartImagePath))
            {
                using var chartImage = Image.FromFile(cert.ChartImagePath);
                graphics.DrawImage(chartImage, chartX, chartY, chartW, chartH);
            }
            else
            {
                graphics.DrawLine(axisPen, chartX + 30, chartY + 14, chartX + 30, chartY + chartH - 26);
                graphics.DrawLine(axisPen, chartX + 30, chartY + chartH - 26, chartX + chartW - 20, chartY + chartH - 26);
                graphics.DrawLine(axisPen, chartX + ((chartW - 50) * 0.25f) + 30, chartY + 14, chartX + ((chartW - 50) * 0.25f) + 30, chartY + chartH - 26);
                graphics.DrawLine(axisPen, chartX + ((chartW - 50) * 0.50f) + 30, chartY + 14, chartX + ((chartW - 50) * 0.50f) + 30, chartY + chartH - 26);
                graphics.DrawLine(axisPen, chartX + ((chartW - 50) * 0.75f) + 30, chartY + 14, chartX + ((chartW - 50) * 0.75f) + 30, chartY + chartH - 26);

                var (writePoints, readPoints) = GetProfilePointsForChart(cert);
                var maxSpeed = Math.Max(writePoints.Count > 0 ? writePoints.Max() : 0, readPoints.Count > 0 ? readPoints.Max() : 0);
                if (maxSpeed <= 0) maxSpeed = 1;

                DrawProfilePolyline(graphics, writePen, writePoints, maxSpeed, chartX + 30, chartY + 14, chartW - 50, chartH - 40);
                DrawProfilePolyline(graphics, readPen, readPoints, maxSpeed, chartX + 30, chartY + 14, chartW - 50, chartH - 40);

                graphics.DrawString("MB/s", smallFont, mutedBrush, chartX - 4, chartY + 8);
                graphics.DrawString($"{maxSpeed:F0}", smallFont, mutedBrush, chartX - 22, chartY + 14);
                graphics.DrawString($"{maxSpeed / 2:F0}", smallFont, mutedBrush, chartX - 22, chartY + (chartH - 40) / 2 + 10);
                graphics.DrawString("0", smallFont, mutedBrush, chartX - 12, chartY + chartH - 30);
                graphics.DrawString("0 %", smallFont, mutedBrush, chartX + 26, chartY + chartH - 18);
                graphics.DrawString("25 %", smallFont, mutedBrush, chartX + (chartW * 0.25f) - 8, chartY + chartH - 18);
                graphics.DrawString("50 %", smallFont, mutedBrush, chartX + chartW / 2 - 8, chartY + chartH - 18);
                graphics.DrawString("75 %", smallFont, mutedBrush, chartX + (chartW * 0.75f) - 8, chartY + chartH - 18);
                graphics.DrawString("100 %", smallFont, mutedBrush, chartX + chartW - 40, chartY + chartH - 18);

                using var pdfLegendWriteBrush = new SolidBrush(Color.FromArgb(220, 38, 38));
                using var pdfLegendReadBrush = new SolidBrush(Color.FromArgb(5, 150, 105));
                graphics.DrawString("Zápis", smallFont, pdfLegendWriteBrush, chartX + chartW - 150, chartY + 8);
                graphics.DrawString("Čtení", smallFont, pdfLegendReadBrush, chartX + chartW - 90, chartY + 8);
            }

            y += 290;
            graphics.DrawString("Doporučení", sectionFont, accentBrush, 48, y);
            y += 34;
            graphics.FillRectangle(panelBrush, 48, y, width - 96, 140);
            graphics.DrawRectangle(borderPen, 48, y, width - 96, 140);
            graphics.DrawString(cert.RecommendationNotes ?? "Není k dispozici", valueFont, textBrush, new RectangleF(64, y + 16, width - 130, 100));

            using var stream = new MemoryStream();
            var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
            bitmap.Save(stream, encoder, parameters);
            return stream.ToArray();
        });
    }

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
        {
            writer.Write($"{offsets[i]:D10} 00000 n \n");
        }

        writer.Write("trailer\n");
        writer.Write($"<< /Size {offsets.Count} /Root 1 0 R >>\n");
        writer.Write("startxref\n");
        writer.Write($"{xrefStart}\n");
        writer.Write("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Downsamples speed samples to a fixed number of points for chart rendering.
    /// </summary>
    private static List<double> DownsampleSpeeds(IEnumerable<double> speeds, int targetPoints)
    {
        var values = speeds.Where(v => v > 0).ToList();
        if (values.Count == 0)
        {
            return new List<double>();
        }

        if (values.Count <= targetPoints)
        {
            return values;
        }

        var result = new List<double>(targetPoints);
        var bucketSize = values.Count / (double)targetPoints;

        for (var i = 0; i < targetPoints; i++)
        {
            var start = (int)Math.Floor(i * bucketSize);
            var end = (int)Math.Floor((i + 1) * bucketSize);
            end = Math.Clamp(end, start + 1, values.Count);

            var sum = 0d;
            var count = 0;
            for (var j = start; j < end; j++)
            {
                sum += values[j];
                count++;
            }

            result.Add(count > 0 ? sum / count : values[Math.Min(start, values.Count - 1)]);
        }

        return result;
    }

    /// <summary>
    /// Appends a polyline stroke to PDF content stream in chart coordinates.
    /// </summary>
    private static void AppendPdfPolyline(
        System.Text.StringBuilder builder,
        IReadOnlyList<double> values,
        double maxValue,
        double xMin,
        double xMax,
        double yMin,
        double yMax,
        string strokeColor)
    {
        if (values.Count < 2)
        {
            return;
        }

        builder.AppendLine(strokeColor);
        builder.AppendLine("1.4 w");

        var step = (xMax - xMin) / (values.Count - 1);
        for (var i = 0; i < values.Count; i++)
        {
            var x = xMin + (i * step);
            var ratio = Math.Clamp(values[i] / maxValue, 0d, 1d);
            var y = yMin + ((yMax - yMin) * ratio);

            if (i == 0)
            {
                builder.AppendLine(FormattableString.Invariant($"{x:0.##} {y:0.##} m"));
            }
            else
            {
                builder.AppendLine(FormattableString.Invariant($"{x:0.##} {y:0.##} l"));
            }
        }

        builder.AppendLine("S");
    }

    /// <summary>
    /// Returns chart points for write/read profiles with sane fallback values.
    /// </summary>
    private static (List<double> Write, List<double> Read) GetProfilePointsForChart(DiskCertificate cert)
    {
        var writePoints = cert.WriteProfilePoints ?? new List<double>();
        var readPoints = cert.ReadProfilePoints ?? new List<double>();

        if (writePoints.Count == 0 && cert.AvgWriteSpeed > 0)
        {
            writePoints = new List<double>
            {
                cert.AvgWriteSpeed,
                cert.MaxWriteSpeed > 0 ? cert.MaxWriteSpeed : cert.AvgWriteSpeed,
                cert.AvgWriteSpeed
            };
        }

        if (readPoints.Count == 0 && cert.AvgReadSpeed > 0)
        {
            readPoints = new List<double>
            {
                cert.AvgReadSpeed,
                cert.MaxReadSpeed > 0 ? cert.MaxReadSpeed : cert.AvgReadSpeed,
                cert.AvgReadSpeed
            };
        }

        return (writePoints, readPoints);
    }

    /// <summary>
    /// Draws profile polyline into GDI+ canvas.
    /// </summary>
    private static void DrawProfilePolyline(
        Graphics graphics,
        Pen pen,
        IReadOnlyList<double> values,
        double maxValue,
        float chartX,
        float chartY,
        float chartWidth,
        float chartHeight)
    {
        if (values.Count < 2)
        {
            return;
        }

        var points = new PointF[values.Count];
        var step = chartWidth / (values.Count - 1);

        for (var i = 0; i < values.Count; i++)
        {
            var x = chartX + (float)(i * step);
            var ratio = (float)Math.Clamp(values[i] / maxValue, 0d, 1d);
            var y = chartY + chartHeight - (ratio * chartHeight);
            points[i] = new PointF(x, y);
        }

        graphics.DrawLines(pen, points);
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

            // Mini výkonový graf (reálná data)
            var chartX = 430f;
            var chartY = 300f;
            var chartW = 420f;
            var chartH = 120f;

            using var axisPen = new Pen(Color.FromArgb(203, 213, 225), 1f);
            graphics.DrawLine(axisPen, chartX, chartY, chartX, chartY + chartH);
            graphics.DrawLine(axisPen, chartX, chartY + chartH, chartX + chartW, chartY + chartH);

            var (writePoints, readPoints) = GetProfilePointsForChart(certificate);
            var maxSpeed = Math.Max(
                writePoints.Count > 0 ? writePoints.Max() : 0,
                readPoints.Count > 0 ? readPoints.Max() : 0);
            if (maxSpeed <= 0)
            {
                maxSpeed = 1;
            }

            if (writePoints.Count > 1)
            {
                using var writePen = new Pen(Color.FromArgb(220, 38, 38), 2f);
                DrawProfilePolyline(graphics, writePen, writePoints, maxSpeed, chartX, chartY, chartW, chartH);
            }

            if (readPoints.Count > 1)
            {
                using var readPen = new Pen(Color.FromArgb(5, 150, 105), 2f);
                DrawProfilePolyline(graphics, readPen, readPoints, maxSpeed, chartX, chartY, chartW, chartH);
            }

            using var legendFont = new Font("Segoe UI", 9, FontStyle.Regular);
            using var legendWriteBrush = new SolidBrush(Color.FromArgb(220, 38, 38));
            using var legendReadBrush = new SolidBrush(Color.FromArgb(5, 150, 105));
            graphics.DrawString("Zápis", legendFont, legendWriteBrush, chartX + 8, chartY - 18);
            graphics.DrawString("Čtení", legendFont, legendReadBrush, chartX + 70, chartY - 18);

            // Popisky os
            graphics.DrawString("MB/s", legendFont, textBrush, chartX - 40, chartY - 2);
            graphics.DrawString("0 %", legendFont, mutedBrush, chartX, chartY + chartH + 4);
            graphics.DrawString("50 %", legendFont, mutedBrush, chartX + (chartW / 2) - 10, chartY + chartH + 4);
            graphics.DrawString("100 %", legendFont, mutedBrush, chartX + chartW - 22, chartY + chartH + 4);

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

    public Task<byte[]> GeneratePreviewAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return Task.Run(() =>
        {
            using var bitmap = new Bitmap(800, 1000);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.Clear(Color.White);

            using var borderPen = new Pen(Color.Navy, 3);
            graphics.DrawRectangle(borderPen, 10, 10, 780, 980);

            using var headerBrush = new SolidBrush(Color.Navy);
            using var headerFont = new Font("Arial", 24, FontStyle.Bold);
            graphics.DrawString("DISK CERTIFICATE", headerFont, headerBrush, 250, 50);

            using var subFont = new Font("Arial", 12);
            using var labelFont = new Font("Arial", 10, FontStyle.Bold);
            using var valueFont = new Font("Arial", 12);
            using var gradeFont = new Font("Arial", 48, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.Black);
            using var gradeBrush = new SolidBrush(GetGradeColor(certificate.Grade));

            graphics.DrawString($"Certificate: {certificate.CertificateNumber}", subFont, textBrush, 30, 100);
            graphics.DrawString($"Generated: {certificate.GeneratedAt:yyyy-MM-dd HH:mm}", subFont, textBrush, 30, 120);

            var y = 160;
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

            y += 20;
            graphics.DrawString("TEST RESULTS", headerFont, headerBrush, 30, y);
            y += 40;

            graphics.DrawString($"Test Type: {certificate.TestType}", valueFont, textBrush, 30, y);
            graphics.DrawString($"Duration: {certificate.TestDuration:hh\\:mm\\:ss}", valueFont, textBrush, 30, y + 25);
            graphics.DrawString($"Errors: {certificate.ErrorCount}", valueFont, textBrush, 30, y + 50);

            y += 90;
            graphics.DrawString("PERFORMANCE", headerFont, headerBrush, 30, y);
            y += 40;

            graphics.DrawString($"Write Speed: {certificate.AvgWriteSpeed:F1} MB/s (max: {certificate.MaxWriteSpeed:F1})", valueFont, textBrush, 30, y);
            graphics.DrawString($"Read Speed: {certificate.AvgReadSpeed:F1} MB/s (max: {certificate.MaxReadSpeed:F1})", valueFont, textBrush, 30, y + 25);
            graphics.DrawString($"Temperature: {certificate.TemperatureRange}", valueFont, textBrush, 30, y + 50);

            y += 100;
            var gradeText = certificate.Grade;
            var gradeSize = graphics.MeasureString(gradeText, gradeFont);
            graphics.DrawString(gradeText, gradeFont, gradeBrush, (800 - gradeSize.Width) / 2, y);

            y += 80;
            using var scoreDisplayFont = new Font("Arial", 16);
            using var healthDisplayFont = new Font("Arial", 14);
            graphics.DrawString($"Skóre: {certificate.Score:F0}/100", scoreDisplayFont, textBrush, (800 - 200) / 2, y);
            graphics.DrawString($"Health: {certificate.HealthStatus}", healthDisplayFont, textBrush, (800 - 200) / 2, y + 30);

            if (certificate.Recommended)
            {
                y += 70;
                using var recFont = new Font("Arial", 14, FontStyle.Bold);
                using var recBrush = new SolidBrush(Color.Green);
                graphics.DrawString("✓ RECOMMENDED", recFont, recBrush, 300, y);
            }

            using var footerFont = new Font("Arial", 10);
            graphics.DrawString("Generated by DiskChecker - Professional Disk Testing Solution", footerFont, textBrush, 200, 950);

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        });
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
                         CERTIFIKÁT DISKU
================================================================================

Číslo certifikátu: {cert.CertificateNumber}
Vygenerováno:      {cert.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC
Vygeneroval:       {cert.GeneratedBy}

--------------------------------------------------------------------------------
                              INFORMACE O DISKU
--------------------------------------------------------------------------------
Model:             {cert.DiskModel}
Sériové číslo:     {cert.SerialNumber}
Kapacita:          {cert.Capacity}
Typ:               {cert.DiskType}
Rozhraní:          {cert.Interface}
Firmware:          {cert.Firmware}

--------------------------------------------------------------------------------
                              VÝSLEDKY TESTU
--------------------------------------------------------------------------------
Typ testu:         {cert.TestType}
Délka testu:       {cert.TestDuration:hh\:mm\:ss}
Chyby:             {cert.ErrorCount}

Známka:            {cert.Grade}
Skóre:             {cert.Score:F0}/100
Stav zdraví:       {cert.HealthStatus}

--------------------------------------------------------------------------------
                            VÝKONNOSTNÍ METRIKY
--------------------------------------------------------------------------------
Rychlost zápisu:   {cert.AvgWriteSpeed:F1} MB/s (max: {cert.MaxWriteSpeed:F1} MB/s)
Rychlost čtení:    {cert.AvgReadSpeed:F1} MB/s (max: {cert.MaxReadSpeed:F1} MB/s)
Teplota:           {cert.TemperatureRange}

--------------------------------------------------------------------------------
                               SMART SOUHRN
--------------------------------------------------------------------------------
SMART v pořádku:   {(cert.SmartPassed ? "Ano" : "Ne")}
Provozní hodiny:   {cert.PowerOnHours}
Počet startů:      {cert.PowerCycles}
Realokované sekt.: {cert.ReallocatedSectors}
Čekající sektory:  {cert.PendingSectors}

--------------------------------------------------------------------------------
                                SANITIZACE
--------------------------------------------------------------------------------
Provedena:         {(cert.SanitizationPerformed ? "Ano" : "Ne")}
Metoda:            {cert.SanitizationMethod ?? "N/A"}
Data ověřena:      {(cert.DataVerified ? "Ano" : "Ne")}
Schéma oddílů:     {cert.PartitionScheme ?? "N/A"}
Souborový systém:  {cert.FileSystem ?? "N/A"}
Název svazku:      {cert.VolumeLabel ?? "N/A"}

--------------------------------------------------------------------------------
                               DOPORUČENÍ
--------------------------------------------------------------------------------
{(cert.Recommended ? "✓ Tento disk je DOPORUČEN k použití." : "⚠ Tento disk má problémy a NENÍ doporučen k použití.")}

{cert.RecommendationNotes}

--------------------------------------------------------------------------------
Tento certifikát byl automaticky vygenerován aplikací DiskChecker.
Před nasazením disku do provozu doporučujeme ověřit aktuální stav.
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
        var grade = session.Grade?.ToUpperInvariant();
        if (grade == "F")
        {
            return "Disk selhal při testování nebo SMART diagnostice. Není doporučen k použití.";
        }

        if (grade == "E")
        {
            return "Disk vykazuje závažný SMART pre-fail stav. Doporučeno pouze k vyřazení nebo dalšímu detailnímu ověření.";
        }

        if (session.Result == TestResult.Pass && session.Score >= 90)
        {
            return "Vynikající stav. Disk je doporučen pro všechny účely.";
        }
        if (session.Result == TestResult.Pass && session.Score >= 70)
        {
            return "Dobrý stav. Disk je vhodný pro běžné použití.";
        }
        if (session.Result == TestResult.Warning)
        {
            return "Byly zjištěny určité problémy. Používejte opatrně. Není doporučeno pro kritická data.";
        }
        if (session.Result == TestResult.Fail)
        {
            return "Disk selhal při testování. Není doporučen k použití. Zvažte jeho výměnu.";
        }
        return "Stav nejistý. Doporučujeme další testování.";
    }

    private static string ResolveDisplaySerial(string? smartSerial, string? storedSerial)
    {
        if (!string.IsNullOrWhiteSpace(smartSerial))
        {
            return smartSerial.Trim();
        }

        if (string.IsNullOrWhiteSpace(storedSerial) ||
            storedSerial.StartsWith("NOSN-", StringComparison.OrdinalIgnoreCase) ||
            storedSerial.Contains('|') ||
            storedSerial.Contains('_'))
        {
            return "N/A";
        }

        return storedSerial;
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

    private static int? GetSmartAttributeValue(TestSession session, int attributeId)
    {
        ArgumentNullException.ThrowIfNull(session);

        var change = session.SmartChanges.FirstOrDefault(c => c.AttributeId == attributeId);
        if (change == null)
        {
            return null;
        }

        return change.ValueAfter > int.MaxValue ? int.MaxValue : (int)change.ValueAfter;
    }

    /// <summary>
    /// Generates and stores a cached chart image for the specified test session.
    /// </summary>
    public Task<string?> GenerateAndStoreChartImageAsync(TestSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (session.Id <= 0)
            {
                return (string?)null;
            }

            var writePoints = DownsampleSpeeds(session.WriteSamples.Select(s => s.SpeedMBps), 32);
            var readPoints = DownsampleSpeeds(session.ReadSamples.Select(s => s.SpeedMBps), 32);
            var tempPoints = session.TemperatureSamples
                .Where(t => t.TemperatureCelsius > 0)
                .OrderBy(t => t.ProgressPercent)
                .ToList();

            if (writePoints.Count == 0 && readPoints.Count == 0)
            {
                return (string?)null;
            }

            var filePath = Path.Combine(_chartCacheDirectory, $"SessionChart_{session.Id}.png");
            RenderChartImage(filePath, writePoints, readPoints, tempPoints);
            return filePath;
        }, cancellationToken);
    }

    /// <summary>
    /// Ensures a cached chart image exists for the specified test session and returns its path.
    /// </summary>
    public async Task<string?> EnsureChartImageAsync(TestSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!string.IsNullOrWhiteSpace(session.ChartImagePath) && File.Exists(session.ChartImagePath))
        {
            return session.ChartImagePath;
        }

        return await GenerateAndStoreChartImageAsync(session, cancellationToken);
    }

    private static void RenderChartImage(
        string filePath,
        IReadOnlyList<double> writePoints,
        IReadOnlyList<double> readPoints,
        IReadOnlyList<TemperatureSample> temperatureSamples)
    {
        const int width = 1040;
        const int height = 360;

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var borderPen = new Pen(Color.FromArgb(210, 215, 223), 2f);
        using var axisPen = new Pen(Color.FromArgb(203, 213, 225), 1f);
        using var gridPen = new Pen(Color.FromArgb(241, 245, 249), 1f);
        using var writePen = new Pen(Color.FromArgb(220, 38, 38), 4f);
        using var readPen = new Pen(Color.FromArgb(5, 150, 105), 4f);
        using var tempPen = new Pen(Color.FromArgb(124, 58, 237), 3f);
        using var textBrush = new SolidBrush(Color.Black);
        using var mutedBrush = new SolidBrush(Color.FromArgb(90, 90, 90));
        using var titleBrush = new SolidBrush(Color.FromArgb(15, 76, 129));
        using var titleFont = new Font("Segoe UI", 15, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 10, FontStyle.Regular);

        graphics.DrawString("Výkonový profil testu", titleFont, titleBrush, 28, 18);

        const float chartX = 72f;
        const float chartY = 58f;
        const float chartW = 930f;
        const float chartH = 230f;

        graphics.DrawRectangle(borderPen, chartX, chartY, chartW, chartH);
        graphics.DrawLine(axisPen, chartX + 30, chartY + 10, chartX + 30, chartY + chartH - 24);
        graphics.DrawLine(axisPen, chartX + 30, chartY + chartH - 24, chartX + chartW - 16, chartY + chartH - 24);

        for (var i = 1; i <= 3; i++)
        {
            var x = chartX + 30 + ((chartW - 46) * i / 4f);
            graphics.DrawLine(gridPen, x, chartY + 10, x, chartY + chartH - 24);
        }

        graphics.DrawLine(gridPen, chartX + 30, chartY + ((chartH - 34) / 2f), chartX + chartW - 16, chartY + ((chartH - 34) / 2f));

        var maxSpeed = Math.Max(writePoints.Count > 0 ? writePoints.Max() : 0, readPoints.Count > 0 ? readPoints.Max() : 0);
        if (maxSpeed <= 0)
        {
            maxSpeed = 1;
        }

        if (writePoints.Count > 1)
        {
            DrawProfilePolyline(graphics, writePen, writePoints, maxSpeed, chartX + 30, chartY + 10, chartW - 46, chartH - 34);
        }

        if (readPoints.Count > 1)
        {
            DrawProfilePolyline(graphics, readPen, readPoints, maxSpeed, chartX + 30, chartY + 10, chartW - 46, chartH - 34);
        }

        if (temperatureSamples.Count > 1)
        {
            var tempValues = temperatureSamples.Select(t => (double)t.TemperatureCelsius).ToList();
            var maxTemp = Math.Max(tempValues.Max(), 1d);
            DrawProfilePolyline(graphics, tempPen, tempValues, maxTemp, chartX + 30, chartY + 10, chartW - 46, chartH - 34);
        }

        graphics.DrawString("MB/s", smallFont, mutedBrush, chartX - 8, chartY + 4);
        graphics.DrawString(FormattableString.Invariant($"{maxSpeed:F0}"), smallFont, mutedBrush, chartX - 24, chartY + 10);
        graphics.DrawString(FormattableString.Invariant($"{maxSpeed / 2:F0}"), smallFont, mutedBrush, chartX - 24, chartY + ((chartH - 34) / 2f));
        graphics.DrawString("0", smallFont, mutedBrush, chartX - 10, chartY + chartH - 30);

        graphics.DrawString("0 %", smallFont, mutedBrush, chartX + 26, chartY + chartH - 16);
        graphics.DrawString("25 %", smallFont, mutedBrush, chartX + chartW * 0.25f, chartY + chartH - 16);
        graphics.DrawString("50 %", smallFont, mutedBrush, chartX + chartW * 0.50f, chartY + chartH - 16);
        graphics.DrawString("75 %", smallFont, mutedBrush, chartX + chartW * 0.75f, chartY + chartH - 16);
        graphics.DrawString("100 %", smallFont, mutedBrush, chartX + chartW - 42, chartY + chartH - 16);

        using var legendWriteBrush = new SolidBrush(Color.FromArgb(220, 38, 38));
        using var legendReadBrush = new SolidBrush(Color.FromArgb(5, 150, 105));
        using var legendTempBrush = new SolidBrush(Color.FromArgb(124, 58, 237));
        graphics.DrawString("Zápis", smallFont, legendWriteBrush, chartX + chartW - 180, chartY + 8);
        graphics.DrawString("Čtení", smallFont, legendReadBrush, chartX + chartW - 120, chartY + 8);
        if (temperatureSamples.Count > 1)
        {
            graphics.DrawString("Teplota", smallFont, legendTempBrush, chartX + chartW - 54, chartY + 8);
        }

        bitmap.Save(filePath, ImageFormat.Png);
    }
}