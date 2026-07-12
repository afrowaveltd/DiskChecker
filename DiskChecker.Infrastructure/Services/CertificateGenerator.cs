using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
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
    private readonly ILocaleProvider? _locale;
    private readonly string _certificatesDirectory;
    private readonly string _labelsDirectory;
    private readonly string _chartCacheDirectory;

    // Chart rendering constants
    private const int MaxChartPoints = 512;
    private const int CertificateChartPoints = 32;

    // Certificate JPEG dimensions (A4 at 150 DPI, extended for all content)
    private const int CertWidth = 1240;
    private const int CertHeight = 2200;

    // Font family fallback chain (cross-platform)
    private const string FontSansSerif = "DejaVu Sans";
    private const string FontSansSerifFallback = "Noto Sans";
    private const string FontSansSerifSystem = "Arial";

    public CertificateGenerator(ILogger<CertificateGenerator>? logger = null, ISettingsService? settingsService = null, ILocaleProvider? locale = null)
    {
        _logger = logger;
        _settingsService = settingsService;
        _locale = locale;

        var basePath = GetCertificateBasePath();
        _certificatesDirectory = Path.Combine(basePath, "Certificates");
        _labelsDirectory = Path.Combine(basePath, "Labels");
        _chartCacheDirectory = Path.Combine(basePath, "ChartCache");

        Directory.CreateDirectory(_certificatesDirectory);
        Directory.CreateDirectory(_labelsDirectory);
        Directory.CreateDirectory(_chartCacheDirectory);

        TryAssignOwnershipToSudoUser(_certificatesDirectory);
        TryAssignOwnershipToSudoUser(_labelsDirectory);
        TryAssignOwnershipToSudoUser(_chartCacheDirectory);
    }

    private string GetCertificateBasePath()
    {
        if (_settingsService != null)
        {
            var customPath = _settingsService.GetCertificatePathAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(customPath) && Directory.Exists(customPath))
                return customPath;
        }

        return GetDefaultCertificateBasePath();
    }

    private static string GetDefaultCertificateBasePath()
    {
        // When the GUI is started via sudo, .NET resolves ApplicationData to
        // /root/.config.  That makes generated PDFs invisible/inaccessible for
        // the desktop user and xdg-open then tries to open a file from /root.
        // Certificates are user-facing documents, so store them under the
        // original sudo user's home instead.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var sudoUser = Environment.GetEnvironmentVariable("SUDO_USER");
            if (!string.IsNullOrWhiteSpace(sudoUser) && !string.Equals(sudoUser, "root", StringComparison.OrdinalIgnoreCase))
            {
                var home = GetOriginalUserHome(sudoUser);
                if (!string.IsNullOrWhiteSpace(home))
                {
                    var documents = Path.Combine(home, "Documents");
                    if (!Directory.Exists(documents))
                    {
                        var localizedDocuments = Path.Combine(home, "Dokumenty");
                        documents = Directory.Exists(localizedDocuments) ? localizedDocuments : home;
                    }

                    return Path.Combine(documents, "DiskChecker");
                }
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiskChecker");
    }

    private static string? GetOriginalUserHome(string sudoUser)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "getent",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("passwd");
            startInfo.ArgumentList.Add(sudoUser);
            using var process = Process.Start(startInfo);

            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(1000);
                var parts = output.Split(':');
                if (parts.Length >= 6 && Directory.Exists(parts[5]))
                    return parts[5];
            }
        }
        catch
        {
            // Fall back below.
        }

        var fallback = Path.Combine("/home", sudoUser);
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static void TryAssignOwnershipToSudoUser(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || string.IsNullOrWhiteSpace(path) || !File.Exists(path) && !Directory.Exists(path))
            return;

        var uid = Environment.GetEnvironmentVariable("SUDO_UID");
        var gid = Environment.GetEnvironmentVariable("SUDO_GID");
        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(gid))
            return;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "chown",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add($"{uid}:{gid}");
            startInfo.ArgumentList.Add(path);
            using var process = Process.Start(startInfo);
            process?.WaitForExit(1000);
        }
        catch
        {
            // Best effort only. If chown is unavailable, root can still write the
            // file and the app will report the full path to the user.
        }
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

            // Do not clone full sample collections here. Large sanitization runs can contain
            // millions of samples; duplicating them for certificate generation was the
            // main source of OutOfMemoryException. Aggregates are calculated in one pass
            // from the original collection (or taken from persisted session aggregates),
            // while chart profiles are downsampled to a small representative set below.
            var writeStats = CalculateSpeedStats(session.WriteSamples);
            var readStats = CalculateSpeedStats(session.ReadSamples);

            var avgWriteSpeed = session.AverageWriteSpeedMBps > 0
                ? session.AverageWriteSpeedMBps
                : writeStats.Average;
            var maxWriteSpeed = session.MaxWriteSpeedMBps > 0
                ? session.MaxWriteSpeedMBps
                : writeStats.Maximum;
            var avgReadSpeed = session.AverageReadSpeedMBps > 0
                ? session.AverageReadSpeedMBps
                : readStats.Average;
            var maxReadSpeed = session.MaxReadSpeedMBps > 0
                ? session.MaxReadSpeedMBps
                : readStats.Maximum;

            var errorCount = session.Errors?.Count ?? 0;
            if (errorCount == 0)
                errorCount = Math.Max(0, session.WriteErrors) + Math.Max(0, session.ReadErrors) + Math.Max(0, session.VerificationErrors);

            var calculated = CalculateGrade(session);
            var grade = SelectWorseGrade(session.Grade, calculated.grade);
            var score = session.Score > 0 ? Math.Min(session.Score, calculated.score) : calculated.score;
            var recommended = IsRecommendedForUse(session, grade, score);

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
                ErrorCount = errorCount,
                AvgWriteSpeed = avgWriteSpeed,
                MaxWriteSpeed = maxWriteSpeed,
                AvgReadSpeed = avgReadSpeed,
                MaxReadSpeed = maxReadSpeed,
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
                Recommended = recommended,
                RecommendationNotes = GenerateRecommendation(session, grade, score, recommended),
                Notes = session.Notes,
                ChartImagePath = session.ChartImagePath
            };

            certificate.WriteProfilePoints = DownsampleSpeedSamples(session.WriteSamples, CertificateChartPoints);
            certificate.ReadProfilePoints = DownsampleSpeedSamples(session.ReadSamples, CertificateChartPoints);
            certificate.TemperatureProfilePoints = DownsampleTemperatures(session.TemperatureSamples ?? new List<TemperatureSample>(), CertificateChartPoints);
            certificate.StallProfilePoints = DownsampleStalls(session.WriteSamples ?? new List<SpeedSample>(), session.ReadSamples ?? new List<SpeedSample>(), CertificateChartPoints);

            certificate.Grade = grade;
            certificate.Score = score;
            certificate.HealthStatus = session.HealthAssessment.ToString();

            PopulateAdvancedTestMetrics(certificate, session);

            if (session.SmartBefore?.Attributes != null)
            {
                foreach (var attr in session.SmartBefore.Attributes)
                {
                    if (IsCriticalAttribute(attr.Id))
                    {
                        certificate.SmartAttributes.Add(new SmartAttributeSummary
                        {
                            // Id is a database-generated primary key — leave it as 0.
                            // attr.Id (e.g. 5, 197, 198) is the SMART attribute ID,
                            // not the entity primary key. Setting it here caused
                            // EF Core tracking conflicts when multiple certificates
                            // had SmartAttributes with the same SMART attribute IDs.
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

        var jpegBytes = await RenderCertificateJpegAsync(certificate, CancellationToken.None);
        var pdfBytes = BuildImagePdfDocument(jpegBytes, CertWidth, CertHeight);
        await File.WriteAllBytesAsync(filePath, pdfBytes);
        TryAssignOwnershipToSudoUser(filePath);

        if (_logger?.IsEnabled(LogLevel.Information) == true)
            _logger.LogInformation("Certificate PDF saved: {Path}", filePath);
        return filePath;
    }

    /// <summary>
    /// Renders the certificate as a JPEG using SkiaSharp (cross-platform).
    /// </summary>
    private Task<byte[]> RenderCertificateJpegAsync(DiskCertificate cert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cert);

        return Task.Run(() =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(CertWidth, CertHeight));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            cancellationToken.ThrowIfCancellationRequested();

            // Resolve fonts with fallback chain
            using var titleFont = ResolveFont(38, bold: true);
            using var sectionFont = ResolveFont(20, bold: true);
            using var labelFont = ResolveFont(20, bold: true);
            using var valueFont = ResolveFont(16, bold: false);
            using var gradeFont = ResolveFont(120, bold: true);
            using var scoreFont = ResolveFont(24, bold: true);
            using var smallFont = ResolveFont(14, bold: false);
            using var chartLabelFont = ResolveFont(13, bold: false);

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
            DrawText(canvas, _locale?.GetString("CertificatePdf.Title", "CERTIFIKÁT KVALITY DISKU") ?? "CERTIFIKÁT KVALITY DISKU", 48, 42, titleFont, accentPaint);
            DrawText(canvas, _locale?.GetString("CertificatePdf.Subtitle", "DiskChecker – Profesionální diagnóza disků") ?? "DiskChecker – Profesionální diagnóza disků", 52, 98, smallFont, mutedPaint);

            // Info panel
            float y = 140;
            canvas.DrawRect(48, y, 760, 300, panelPaint);
            canvas.DrawRect(48, y, 760, 300, borderPaint);

            void DrawLine(string label, string value, int row)
            {
                float yy = y + 18 + row * 34;
                DrawText(canvas, label, 64, yy, labelFont, textPaint);
                float labelWidth = labelFont.MeasureText(label);
                DrawText(canvas, value, 64 + labelWidth + 12, yy, valueFont, textPaint);
            }

            DrawLine(_locale?.GetString("CertificatePdf.Model", "Model:") ?? "Model:", cert.DiskModel, 0);
            DrawLine(_locale?.GetString("CertificatePdf.SerialNumber", "Sériové číslo:") ?? "Sériové číslo:", cert.SerialNumber, 1);
            DrawLine(_locale?.GetString("CertificatePdf.Capacity", "Kapacita:") ?? "Kapacita:", cert.Capacity, 2);
            DrawLine(_locale?.GetString("CertificatePdf.DiskType", "Typ disku:") ?? "Typ disku:", cert.DiskType, 3);
            DrawLine(_locale?.GetString("CertificatePdf.PowerOnHours", "Provozní hodiny:") ?? "Provozní hodiny:", cert.PowerOnHours > 0 ? cert.PowerOnHours.ToString("#,0", CultureInfo.InvariantCulture) : _locale?.GetString("CertificatePdf.NA", "N/A") ?? "N/A", 4);
            DrawLine(_locale?.GetString("CertificatePdf.PowerCycles", "Počet startů:") ?? "Počet startů:", cert.PowerCycles > 0 ? cert.PowerCycles.ToString("#,0", CultureInfo.InvariantCulture) : _locale?.GetString("CertificatePdf.NA", "N/A") ?? "N/A", 5);
            DrawLine(_locale?.GetString("CertificatePdf.CertificateNumber", "Číslo certifikátu:") ?? "Číslo certifikátu:", cert.CertificateNumber, 6);
            DrawLine(_locale?.GetString("CertificatePdf.GeneratedAt", "Vygenerováno:") ?? "Vygenerováno:", cert.GeneratedAt.ToString("dd.MM.yyyy HH:mm"), 7);

            // Grade seal
            canvas.DrawRect(838, y, 350, 300, panelPaint);
            canvas.DrawRect(838, y, 350, 300, borderPaint);
            DrawText(canvas, _locale?.GetString("CertificatePdf.FinalGrade", "KONEČNÁ ZNÁMKA") ?? "KONEČNÁ ZNÁMKA", 926, y + 18, labelFont, textPaint);

            const float sealSize = 150f;
            float sealX = 943f;
            float sealY = y + 78f;

            var gradeText = cert.Grade;
            float gradeTextWidth = gradeFont.MeasureText(gradeText);
            float gradeX = sealX + (sealSize - gradeTextWidth) / 2f;
            // Center vertically: DrawText adds font.Size, so we subtract it here
            // to place baseline correctly for visual centering
            float gradeY = sealY + (sealSize - gradeFont.Size) / 2f;
            DrawText(canvas, gradeText, gradeX, gradeY, gradeFont, gradePaint);

            var scoreText = string.Format(_locale?.GetString("CertificatePdf.Score", "Skóre: {0}/100") ?? "Skóre: {0}/100", cert.Score);
            float scoreWidth = scoreFont.MeasureText(scoreText);
            DrawText(canvas, scoreText, sealX + (sealSize - scoreWidth) / 2f, sealY + sealSize + 8f, scoreFont, textPaint);

            cancellationToken.ThrowIfCancellationRequested();

            // Test results
            y += 330;
            DrawText(canvas, _locale?.GetString("CertificatePdf.TestResults", "Výsledky testu") ?? "Výsledky testu", 48, y, sectionFont, accentPaint);
            y += 34;

            var isSanitizationTest = cert.SanitizationPerformed
                || string.Equals(cert.TestType, TestType.Sanitization.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(cert.TestType, TestType.AbsoluteDestructive.ToString(), StringComparison.OrdinalIgnoreCase);
            var isSeekTest = cert.SeekAvgLatencyMs.HasValue
                || !string.IsNullOrWhiteSpace(cert.SeekTestSummary)
                || string.Equals(cert.TestType, TestType.Seek.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(cert.TestType, TestType.AbsoluteDestructive.ToString(), StringComparison.OrdinalIgnoreCase);
            var testResultsHeight = isSanitizationTest || isSeekTest ? 250 : 170;
            var notAvailableText = _locale?.GetString("CertificatePdf.NA", "N/A") ?? "N/A";

            canvas.DrawRect(48, y, CertWidth - 96, testResultsHeight, panelPaint);
            canvas.DrawRect(48, y, CertWidth - 96, testResultsHeight, borderPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.TestType", "Typ: {0}") ?? "Typ: {0}", cert.TestType), 64, y + 16, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.TestDuration", "Doba: {0}") ?? "Doba: {0}", cert.TestDuration.ToString(@"hh\:mm\:ss")), 64, y + 46, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.ErrorCount", "Chyby: {0}") ?? "Chyby: {0}", cert.ErrorCount), 64, y + 76, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.TemperatureRange", "Teplota: {0}") ?? "Teplota: {0}", cert.TemperatureRange), 64, y + 106, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.AvgWriteSpeed", "Průměrný zápis: {0} MB/s") ?? "Průměrný zápis: {0} MB/s", FormatSpeedForPdf(cert.AvgWriteSpeed, notAvailableText)), 520, y + 16, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.AvgReadSpeed", "Průměrné čtení: {0} MB/s") ?? "Průměrné čtení: {0} MB/s", FormatSpeedForPdf(cert.AvgReadSpeed, notAvailableText)), 520, y + 46, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.HealthStatus", "Stav: {0}") ?? "Stav: {0}", cert.HealthStatus), 520, y + 76, valueFont, textPaint);

            if (isSanitizationTest)
            {
                var verificationText = cert.DataVerified
                    ? _locale?.GetString("CertificatePdf.Yes", "Ano") ?? "Ano"
                    : _locale?.GetString("CertificatePdf.No", "Ne") ?? "Ne";

                DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.SanitizationMethod", "Metoda sanitizace: {0}") ?? "Metoda sanitizace: {0}", string.IsNullOrWhiteSpace(cert.SanitizationMethod) ? notAvailableText : cert.SanitizationMethod), 64, y + 136, valueFont, textPaint);
                DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.DataVerified", "Verifikace dat: {0}") ?? "Verifikace dat: {0}", verificationText), 64, y + 166, valueFont, textPaint);
                DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.PartitionScheme", "Schéma oddílů: {0}") ?? "Schéma oddílů: {0}", string.IsNullOrWhiteSpace(cert.PartitionScheme) ? notAvailableText : cert.PartitionScheme), 64, y + 196, valueFont, textPaint);
                DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.FileSystem", "Souborový systém: {0}") ?? "Souborový systém: {0}", string.IsNullOrWhiteSpace(cert.FileSystem) ? notAvailableText : cert.FileSystem), 520, y + 136, valueFont, textPaint);
                DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.VolumeLabel", "Název svazku: {0}") ?? "Název svazku: {0}", string.IsNullOrWhiteSpace(cert.VolumeLabel) ? notAvailableText : cert.VolumeLabel), 520, y + 166, valueFont, textPaint);
            }

            if (isSeekTest)
            {
                var seekY = isSanitizationTest ? y + 196 : y + 136;
                DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.SeekAvgLatency", "Průměrná seek latence: {0} ms") ?? "Průměrná seek latence: {0} ms", cert.SeekAvgLatencyMs.HasValue ? cert.SeekAvgLatencyMs.Value.ToString("F2", CultureInfo.InvariantCulture) : notAvailableText), 520, seekY, valueFont, textPaint);
                DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.SeekP95Latency", "P95 seek latence: {0} ms") ?? "P95 seek latence: {0} ms", cert.SeekP95LatencyMs.HasValue ? cert.SeekP95LatencyMs.Value.ToString("F2", CultureInfo.InvariantCulture) : notAvailableText), 520, seekY + 30, valueFont, textPaint);
                if (!isSanitizationTest && !string.IsNullOrWhiteSpace(cert.SeekTestSummary))
                    DrawTextWrapped(canvas, cert.SeekTestSummary, 64, y + 166, 430, 60, smallFont, textPaint);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // SMART summary
            y += testResultsHeight + 20;
            DrawText(canvas, _locale?.GetString("CertificatePdf.SmartSummary", "SMART souhrn") ?? "SMART souhrn", 48, y, sectionFont, accentPaint);
            y += 34;
            canvas.DrawRect(48, y, CertWidth - 96, 190, panelPaint);
            canvas.DrawRect(48, y, CertWidth - 96, 190, borderPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.SmartPowerOnHours", "Provozní hodiny: {0}") ?? "Provozní hodiny: {0}", cert.PowerOnHours > 0 ? cert.PowerOnHours.ToString("#,0", CultureInfo.InvariantCulture) : _locale?.GetString("CertificatePdf.NA", "N/A") ?? "N/A"), 64, y + 16, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.SmartPowerCycles", "Počet startů: {0}") ?? "Počet startů: {0}", cert.PowerCycles > 0 ? cert.PowerCycles.ToString("#,0", CultureInfo.InvariantCulture) : _locale?.GetString("CertificatePdf.NA", "N/A") ?? "N/A"), 64, y + 44, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.SmartReallocatedSectors", "Realokované sektory: {0}") ?? "Realokované sektory: {0}", cert.ReallocatedSectors), 520, y + 16, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.SmartPendingSectors", "Čekající sektory: {0}") ?? "Čekající sektory: {0}", cert.PendingSectors), 520, y + 44, valueFont, textPaint);
            DrawText(canvas, _locale?.GetString("CertificatePdf.GradeLegend", "Legenda známek:") ?? "Legenda známek:", 64, y + 82, valueFont, accentPaint);
            DrawText(canvas, _locale?.GetString("CertificatePdf.GradeAB", "A = výborný stav | B = velmi dobrý stav") ?? "A = výborný stav | B = velmi dobrý stav", 64, y + 110, smallFont, textPaint);
            DrawText(canvas, _locale?.GetString("CertificatePdf.GradeCD", "C = dobrý stav | D = opotřebený disk") ?? "C = dobrý stav | D = opotřebený disk", 64, y + 132, smallFont, textPaint);
            DrawText(canvas, _locale?.GetString("CertificatePdf.GradeEF", "E = rizikový disk | F = kritický / vadný") ?? "E = rizikový disk | F = kritický / vadný", 64, y + 154, smallFont, textPaint);

            cancellationToken.ThrowIfCancellationRequested();

            // Performance chart(s)
            y += 220;
            var isSeekChart = cert.SeekLatencyPoints.Count > 0;
            var (writePoints, readPoints) = GetProfilePointsForChart(cert);
            var hasSpeedData = writePoints.Count > 0 || readPoints.Count > 0;
            var hasBothCharts = isSeekChart && hasSpeedData;

            float chartX = 72f;
            float chartW = CertWidth - 160f;

            if (hasBothCharts)
            {
                // Dual-chart layout for AbsoluteDestructive tests:
                // Top: Sanitization speed profile (write/read overlay)
                // Bottom: Seek latency scatter chart
                float topChartH = 200f;
                float bottomChartH = 200f;
                float gapBetweenCharts = 30f;

                // --- Top chart: Sanitization speed profile ---
                var topTitle = _locale?.GetString("CertificatePdf.SanitizationProfile", "Profil sanitizace") ?? "Profil sanitizace";
                DrawText(canvas, topTitle, 48, y, sectionFont, accentPaint);
                y += 34;
                float topChartY = y;
                canvas.DrawRect(chartX, topChartY, chartW, topChartH, borderPaint);

                var maxSpeed = Math.Max(writePoints.Count > 0 ? writePoints.Max() : 0, readPoints.Count > 0 ? readPoints.Max() : 0);
                if (maxSpeed <= 0) maxSpeed = 1;
                canvas.DrawLine(chartX + 30, topChartY + 14, chartX + 30, topChartY + topChartH - 26, axisPen);
                canvas.DrawLine(chartX + 30, topChartY + topChartH - 26, chartX + chartW - 20, topChartY + topChartH - 26, axisPen);
                for (int g = 1; g <= 3; g++)
                {
                    float gx = chartX + 30 + (chartW - 50) * g / 4f;
                    canvas.DrawLine(gx, topChartY + 14, gx, topChartY + topChartH - 26, axisPen);
                }
                DrawProfilePolyline(canvas, writePen, writePoints, maxSpeed, chartX + 30, topChartY + 14, chartW - 50, topChartH - 40);
                DrawProfilePolyline(canvas, readPen, readPoints, maxSpeed, chartX + 30, topChartY + 14, chartW - 50, topChartH - 40);
                DrawText(canvas, _locale?.GetString("CertificatePdf.Mbps", "MB/s") ?? "MB/s", chartX - 4, topChartY + 8, chartLabelFont, mutedPaint);
                DrawText(canvas, $"{maxSpeed:F0}", chartX - 22, topChartY + 14, chartLabelFont, mutedPaint);
                DrawText(canvas, $"{maxSpeed / 2:F0}", chartX - 22, topChartY + (topChartH - 40) / 2 + 10, chartLabelFont, mutedPaint);
                DrawText(canvas, "0", chartX - 12, topChartY + topChartH - 30, chartLabelFont, mutedPaint);
                DrawText(canvas, _locale?.GetString("CertificatePdf.Percent0", "0 %") ?? "0 %", chartX + 26, topChartY + topChartH - 18, chartLabelFont, mutedPaint);
                DrawText(canvas, _locale?.GetString("CertificatePdf.Percent50", "50 %") ?? "50 %", chartX + chartW / 2 - 8, topChartY + topChartH - 18, chartLabelFont, mutedPaint);
                DrawText(canvas, _locale?.GetString("CertificatePdf.Percent100", "100 %") ?? "100 %", chartX + chartW - 40, topChartY + topChartH - 18, chartLabelFont, mutedPaint);
                using var legendWritePaint = new SKPaint { Color = new SKColor(220, 38, 38), IsAntialias = true };
                using var legendReadPaint = new SKPaint { Color = new SKColor(5, 150, 105), IsAntialias = true };
                DrawText(canvas, _locale?.GetString("CertificatePdf.Write", "Zápis") ?? "Zápis", chartX + chartW - 150, topChartY + 8, chartLabelFont, legendWritePaint);
                DrawText(canvas, _locale?.GetString("CertificatePdf.Read", "Čtení") ?? "Čtení", chartX + chartW - 90, topChartY + 8, chartLabelFont, legendReadPaint);

                // --- Bottom chart: Seek latency scatter ---
                y = topChartY + topChartH + gapBetweenCharts;
                var bottomTitle = _locale?.GetString("CertificatePdf.SeekLatencyChart", "Seek latence") ?? "Seek latence";
                DrawText(canvas, bottomTitle, 48, y, sectionFont, accentPaint);
                y += 34;
                float bottomChartY = y;
                canvas.DrawRect(chartX, bottomChartY, chartW, bottomChartH, borderPaint);

                var latencies = cert.SeekLatencyPoints.Where(v => v > 0).ToList();
                if (latencies.Count > 0)
                {
                    var maxLatency = latencies.Max();
                    var yMax = Math.Max(maxLatency * 1.15, maxLatency + 1);
                    canvas.DrawLine(chartX + 30, bottomChartY + 14, chartX + 30, bottomChartY + bottomChartH - 26, axisPen);
                    canvas.DrawLine(chartX + 30, bottomChartY + bottomChartH - 26, chartX + chartW - 20, bottomChartY + bottomChartH - 26, axisPen);
                    for (int g = 1; g <= 3; g++)
                    {
                        float gy = bottomChartY + 14 + (bottomChartH - 40) * g / 4f;
                        canvas.DrawLine(chartX + 30, gy, chartX + chartW - 20, gy, axisPen);
                    }
                    using var dotPaint = new SKPaint { Color = new SKColor(220, 38, 38), Style = SKPaintStyle.Fill, IsAntialias = true };
                    float plotW = chartW - 50;
                    float plotH = bottomChartH - 40;
                    for (var i = 0; i < latencies.Count; i++)
                    {
                        float sx = chartX + 30 + (i / (float)Math.Max(1, latencies.Count - 1)) * plotW;
                        float sy = bottomChartY + 14 + plotH - (float)(latencies[i] / yMax * plotH);
                        canvas.DrawCircle(sx, sy, 2.4f, dotPaint);
                    }
                    DrawText(canvas, _locale?.GetString("CertificatePdf.Ms", "ms") ?? "ms", chartX - 4, bottomChartY + 8, chartLabelFont, mutedPaint);
                    DrawText(canvas, $"{yMax:F1}", chartX - 22, bottomChartY + 14, chartLabelFont, mutedPaint);
                    DrawText(canvas, $"{yMax / 2:F1}", chartX - 22, bottomChartY + (bottomChartH - 40) / 2 + 10, chartLabelFont, mutedPaint);
                    DrawText(canvas, "0", chartX - 12, bottomChartY + bottomChartH - 30, chartLabelFont, mutedPaint);
                    DrawText(canvas, "1", chartX + 26, bottomChartY + bottomChartH - 18, chartLabelFont, mutedPaint);
                    var midLabel = Math.Max(1, latencies.Count / 2).ToString(CultureInfo.InvariantCulture);
                    DrawText(canvas, midLabel, chartX + 30 + plotW / 2f - 8, bottomChartY + bottomChartH - 18, chartLabelFont, mutedPaint);
                    DrawText(canvas, latencies.Count.ToString(CultureInfo.InvariantCulture), chartX + chartW - 40, bottomChartY + bottomChartH - 18, chartLabelFont, mutedPaint);
                    using var legendDotPaint = new SKPaint { Color = new SKColor(220, 38, 38), IsAntialias = true };
                    DrawText(canvas, _locale?.GetString("CertificatePdf.Latency", "Latence") ?? "Latence", chartX + chartW - 120, bottomChartY + 8, chartLabelFont, legendDotPaint);
                }
                else
                {
                    var noDataText = _locale?.GetString("CertificatePdf.NoChartData", "Data nejsou k dispozici") ?? "Data nejsou k dispozici";
                    using var noDataFont = ResolveFont(18, bold: false);
                    float textWidth = noDataFont.MeasureText(noDataText);
                    float textX = chartX + (chartW - textWidth) / 2f;
                    float textY = bottomChartY + bottomChartH / 2f - noDataFont.Size / 2f;
                    DrawText(canvas, noDataText, textX, textY, noDataFont, mutedPaint);
                }

                y = bottomChartY + bottomChartH;
            }
            else
            {
                // Single-chart layout (original behavior)
                var chartTitle = isSeekChart
                    ? (_locale?.GetString("CertificatePdf.SeekLatencyChart", "Seek latence") ?? "Seek latence")
                    : (_locale?.GetString("CertificatePdf.PerformanceProfile", "Výkonový profil testu") ?? "Výkonový profil testu");
                DrawText(canvas, chartTitle, 48, y, sectionFont, accentPaint);
                y += 34;

                float chartY = y;
                float chartH = 260f;
                canvas.DrawRect(chartX, chartY, chartW, chartH, borderPaint);

                if (!string.IsNullOrWhiteSpace(cert.ChartImagePath) && File.Exists(cert.ChartImagePath))
                {
                    using var chartImage = SKBitmap.Decode(cert.ChartImagePath);
                    if (chartImage != null)
                        canvas.DrawBitmap(chartImage, new SKRect(chartX, chartY, chartX + chartW, chartY + chartH));
                }
                else if (isSeekChart)
                {
                    var latencies = cert.SeekLatencyPoints.Where(v => v > 0).ToList();
                    if (latencies.Count > 0)
                    {
                        var maxLatency = latencies.Max();
                        var yMax = Math.Max(maxLatency * 1.15, maxLatency + 1);
                        canvas.DrawLine(chartX + 30, chartY + 14, chartX + 30, chartY + chartH - 26, axisPen);
                        canvas.DrawLine(chartX + 30, chartY + chartH - 26, chartX + chartW - 20, chartY + chartH - 26, axisPen);
                        for (int g = 1; g <= 3; g++)
                        {
                            float gy = chartY + 14 + (chartH - 40) * g / 4f;
                            canvas.DrawLine(chartX + 30, gy, chartX + chartW - 20, gy, axisPen);
                        }
                        using var dotPaint = new SKPaint { Color = new SKColor(220, 38, 38), Style = SKPaintStyle.Fill, IsAntialias = true };
                        float plotW = chartW - 50;
                        float plotH = chartH - 40;
                        for (var i = 0; i < latencies.Count; i++)
                        {
                            float sx = chartX + 30 + (i / (float)Math.Max(1, latencies.Count - 1)) * plotW;
                            float sy = chartY + 14 + plotH - (float)(latencies[i] / yMax * plotH);
                            canvas.DrawCircle(sx, sy, 2.4f, dotPaint);
                        }
                        DrawText(canvas, _locale?.GetString("CertificatePdf.Ms", "ms") ?? "ms", chartX - 4, chartY + 8, chartLabelFont, mutedPaint);
                        DrawText(canvas, $"{yMax:F1}", chartX - 22, chartY + 14, chartLabelFont, mutedPaint);
                        DrawText(canvas, $"{yMax / 2:F1}", chartX - 22, chartY + (chartH - 40) / 2 + 10, chartLabelFont, mutedPaint);
                        DrawText(canvas, "0", chartX - 12, chartY + chartH - 30, chartLabelFont, mutedPaint);
                        DrawText(canvas, "1", chartX + 26, chartY + chartH - 18, chartLabelFont, mutedPaint);
                        var midLabel = Math.Max(1, latencies.Count / 2).ToString(CultureInfo.InvariantCulture);
                        DrawText(canvas, midLabel, chartX + 30 + plotW / 2f - 8, chartY + chartH - 18, chartLabelFont, mutedPaint);
                        DrawText(canvas, latencies.Count.ToString(CultureInfo.InvariantCulture), chartX + chartW - 40, chartY + chartH - 18, chartLabelFont, mutedPaint);
                        using var legendDotPaint = new SKPaint { Color = new SKColor(220, 38, 38), IsAntialias = true };
                        DrawText(canvas, _locale?.GetString("CertificatePdf.Latency", "Latence") ?? "Latence", chartX + chartW - 120, chartY + 8, chartLabelFont, legendDotPaint);
                    }
                    else
                    {
                        var noDataText = _locale?.GetString("CertificatePdf.NoChartData", "Data nejsou k dispozici") ?? "Data nejsou k dispozici";
                        using var noDataFont = ResolveFont(18, bold: false);
                        float textWidth = noDataFont.MeasureText(noDataText);
                        float textX = chartX + (chartW - textWidth) / 2f;
                        float textY = chartY + chartH / 2f - noDataFont.Size / 2f;
                        DrawText(canvas, noDataText, textX, textY, noDataFont, mutedPaint);
                    }
                }
                else
                {
                    canvas.DrawLine(chartX + 30, chartY + 14, chartX + 30, chartY + chartH - 26, axisPen);
                    canvas.DrawLine(chartX + 30, chartY + chartH - 26, chartX + chartW - 20, chartY + chartH - 26, axisPen);
                    for (int g = 1; g <= 3; g++)
                    {
                        float gx = chartX + 30 + (chartW - 50) * g / 4f;
                        canvas.DrawLine(gx, chartY + 14, gx, chartY + chartH - 26, axisPen);
                    }
                    var maxSpeed = 1.0;
                    if (hasSpeedData)
                    {
                        maxSpeed = Math.Max(writePoints.Count > 0 ? writePoints.Max() : 0, readPoints.Count > 0 ? readPoints.Max() : 0);
                        if (maxSpeed <= 0) maxSpeed = 1;
                        DrawProfilePolyline(canvas, writePen, writePoints, maxSpeed, chartX + 30, chartY + 14, chartW - 50, chartH - 40);
                        DrawProfilePolyline(canvas, readPen, readPoints, maxSpeed, chartX + 30, chartY + 14, chartW - 50, chartH - 40);
                        if (cert.StallProfilePoints.Count > 0)
                        {
                            using var stallPaint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Fill, IsAntialias = true };
                            var stallCount = cert.StallProfilePoints.Count;
                            for (var si = 0; si < stallCount; si++)
                            {
                                if (cert.StallProfilePoints[si])
                                {
                                    float sx = chartX + 30 + (chartW - 50) * si / (float)Math.Max(1, stallCount - 1);
                                    float sy = chartY + chartH - 26;
                                    canvas.DrawCircle(sx, sy, 4f, stallPaint);
                                }
                            }
                            using var legendStallPaint = new SKPaint { Color = SKColors.Red, IsAntialias = true };
                            DrawText(canvas, _locale?.GetString("CertificatePdf.Stall", "ZamrznutĂ­") ?? "ZamrznutĂ­", chartX + chartW - 150, chartY + 24, chartLabelFont, legendStallPaint);
                        }
                    }
                    else
                    {
                        var noDataText = _locale?.GetString("CertificatePdf.NoChartData", "Data nejsou k dispozici") ?? "Data nejsou k dispozici";
                        using var noDataFont = ResolveFont(18, bold: false);
                        float textWidth = noDataFont.MeasureText(noDataText);
                        float textX = chartX + (chartW - textWidth) / 2f;
                        float textY = chartY + chartH / 2f - noDataFont.Size / 2f;
                        DrawText(canvas, noDataText, textX, textY, noDataFont, mutedPaint);
                    }
                    DrawText(canvas, _locale?.GetString("CertificatePdf.Mbps", "MB/s") ?? "MB/s", chartX - 4, chartY + 8, chartLabelFont, mutedPaint);
                    DrawText(canvas, $"{maxSpeed:F0}", chartX - 22, chartY + 14, chartLabelFont, mutedPaint);
                    DrawText(canvas, $"{maxSpeed / 2:F0}", chartX - 22, chartY + (chartH - 40) / 2 + 10, chartLabelFont, mutedPaint);
                    DrawText(canvas, "0", chartX - 12, chartY + chartH - 30, chartLabelFont, mutedPaint);
                    DrawText(canvas, _locale?.GetString("CertificatePdf.Percent0", "0 %") ?? "0 %", chartX + 26, chartY + chartH - 18, chartLabelFont, mutedPaint);
                    DrawText(canvas, _locale?.GetString("CertificatePdf.Percent25", "25 %") ?? "25 %", chartX + (chartW * 0.25f) - 8, chartY + chartH - 18, chartLabelFont, mutedPaint);
                    DrawText(canvas, _locale?.GetString("CertificatePdf.Percent50", "50 %") ?? "50 %", chartX + chartW / 2 - 8, chartY + chartH - 18, chartLabelFont, mutedPaint);
                    DrawText(canvas, _locale?.GetString("CertificatePdf.Percent75", "75 %") ?? "75 %", chartX + (chartW * 0.75f) - 8, chartY + chartH - 18, chartLabelFont, mutedPaint);
                    DrawText(canvas, _locale?.GetString("CertificatePdf.Percent100", "100 %") ?? "100 %", chartX + chartW - 40, chartY + chartH - 18, chartLabelFont, mutedPaint);
                    using var legendWritePaint = new SKPaint { Color = new SKColor(220, 38, 38), IsAntialias = true };
                    using var legendReadPaint = new SKPaint { Color = new SKColor(5, 150, 105), IsAntialias = true };
                    DrawText(canvas, _locale?.GetString("CertificatePdf.Write", "ZĂˇpis") ?? "ZĂˇpis", chartX + chartW - 150, chartY + 8, chartLabelFont, legendWritePaint);
                    DrawText(canvas, _locale?.GetString("CertificatePdf.Read", "ÄŚtenĂ­") ?? "ÄŚtenĂ­", chartX + chartW - 90, chartY + 8, chartLabelFont, legendReadPaint);
                }

                y = chartY + chartH;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Recommendation// Recommendation
            y += 290;
            DrawText(canvas, _locale?.GetString("CertificatePdf.Recommendations", "Doporučení") ?? "Doporučení", 48, y, sectionFont, accentPaint);
            y += 34;
            canvas.DrawRect(48, y, CertWidth - 96, 140, panelPaint);
            canvas.DrawRect(48, y, CertWidth - 96, 140, borderPaint);
            DrawTextWrapped(canvas, cert.RecommendationNotes ?? _locale?.GetString("CertificatePdf.NotAvailable", "Není k dispozici") ?? "Není k dispozici", 64, y + 16, CertWidth - 130, 100, valueFont, textPaint);

            cancellationToken.ThrowIfCancellationRequested();

            // Diagnostic notes
            if (!string.IsNullOrWhiteSpace(cert.Notes))
            {
                y += 160;
                DrawText(canvas, _locale?.GetString("CertificatePdf.Reasons", "Důvody hodnocení") ?? "Důvody hodnocení", 48, y, sectionFont, accentPaint);
                y += 34;
                float notesHeight = Math.Max(60, valueFont.MeasureText(cert.Notes) / (CertWidth - 130) * valueFont.Size * 1.5f + 20);
                canvas.DrawRect(48, y, CertWidth - 96, notesHeight, panelPaint);
                canvas.DrawRect(48, y, CertWidth - 96, notesHeight, borderPaint);
                DrawTextWrapped(canvas, cert.Notes, 64, y + 16, CertWidth - 130, notesHeight - 20, valueFont, textPaint);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Encode as JPEG
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Resolves a font with cross-platform fallback chain.
    /// First tries embedded DejaVu Sans (works everywhere), then system fonts.
    /// </summary>
    private static SKFont ResolveFont(float size, bool bold)
    {
        var weight = bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;

        // 1. Try embedded DejaVu Sans font (works on all platforms)
        var embeddedTypeface = LoadEmbeddedTypeface(bold);
        if (embeddedTypeface != null)
        {
            return new SKFont(embeddedTypeface, size);
        }

        // 2. Try system fonts as fallback
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

        // 3. Ultimate fallback: system default
        return new SKFont(SKTypeface.Default, size);
    }

    /// <summary>
    /// Loads the embedded DejaVu Sans TTF font from assembly resources.
    /// Returns null if the embedded font is not available.
    /// </summary>
    private static SKTypeface? LoadEmbeddedTypeface(bool bold)
    {
        try
        {
            var resourceName = bold ? "DiskChecker.Infrastructure.Resources.DejaVuSans-Bold.ttf"
                                     : "DiskChecker.Infrastructure.Resources.DejaVuSans.ttf";
            var assembly = typeof(CertificateGenerator).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                // Copy to byte array so SkiaSharp can own the data
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var fontBytes = ms.ToArray();
                return SKTypeface.FromStream(new MemoryStream(fontBytes));
            }
        }
        catch
        {
            // Ignore — fall back to system fonts
        }
        return null;
    }

    private static string SanitizePdfText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sanitized = text
            .Replace("✅", "[OK]")
            .Replace("❌", "[X]")
            .Replace("⚠️", "[!]")
            .Replace("⚠", "[!]")
            .Replace("🌡", "Teplota")
            .Replace("⏱", "Cas")
            .Replace("🔋", "Wear")
            .Replace("🔧", "")
            .Replace("→", "->")
            .Replace("⇒", "=>")
            .Replace("–", "-")
            .Replace("—", "-")
            .Replace("−", "-")
            .Replace("•", "-")
            .Replace("Δ", "delta")
            .Replace("：", ":")
            .Replace("️", string.Empty);

        // Drop unsupported non-BMP symbols (mostly emoji). Keep Czech/Latin text.
        return string.Concat(sanitized.EnumerateRunes()
            .Where(r => r.Value <= 0xFFFF)
            .Select(r => r.ToString()));
    }

    private static void DrawText(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint)
    {
        canvas.DrawText(SanitizePdfText(text), x, y + font.Size, font, paint);
    }

    private static void DrawTextWrapped(SKCanvas canvas, string text, float x, float y, float maxWidth, float maxHeight, SKFont font, SKPaint paint)
    {
        // Simple word-wrap using SkiaSharp text measurement.  Emoji and several
        // symbol glyphs are intentionally normalized first because our embedded
        // DejaVu font does not contain them; Skia would render empty squares in
        // certificate notes/reasons.
        text = SanitizePdfText(text);
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

    private static (int Count, double Average, double Maximum) CalculateSpeedStats(IReadOnlyList<SpeedSample>? samples)
    {
        if (samples == null || samples.Count == 0)
            return (0, 0, 0);

        var count = 0;
        var sum = 0d;
        var max = 0d;

        foreach (var sample in samples)
        {
            var speed = sample.SpeedMBps;
            if (speed <= 0)
                continue;

            count++;
            sum += speed;
            if (speed > max)
                max = speed;
        }

        return count == 0 ? (0, 0, 0) : (count, sum / count, max);
    }

    /// <summary>
    /// Downsamples speed samples without materializing another full list.
    /// Each output bucket stores the bucket average; very short dips/peaks are
    /// still represented because the max/min metrics are stored separately on
    /// the certificate and are not derived from this chart-only profile.
    /// </summary>
    private static List<double> DownsampleSpeedSamples(IReadOnlyList<SpeedSample>? samples, int targetPoints)
    {
        if (samples == null || samples.Count == 0)
            return new List<double>();

        var positiveCount = 0;
        foreach (var sample in samples)
        {
            if (sample.SpeedMBps > 0)
                positiveCount++;
        }

        if (positiveCount == 0)
            return new List<double>();

        if (positiveCount <= targetPoints)
        {
            var values = new List<double>(positiveCount);
            foreach (var sample in samples)
            {
                if (sample.SpeedMBps > 0)
                    values.Add(sample.SpeedMBps);
            }
            return values;
        }

        var result = new List<double>(targetPoints);
        var bucketSize = positiveCount / (double)targetPoints;
        var positiveIndex = 0;
        var bucket = 0;
        var bucketEnd = (int)Math.Floor((bucket + 1) * bucketSize);
        var sum = 0d;
        var count = 0;

        foreach (var sample in samples)
        {
            if (sample.SpeedMBps <= 0)
                continue;

            while (bucket < targetPoints - 1 && positiveIndex >= bucketEnd)
            {
                result.Add(count > 0 ? sum / count : 0);
                bucket++;
                bucketEnd = (int)Math.Floor((bucket + 1) * bucketSize);
                sum = 0d;
                count = 0;
            }

            sum += sample.SpeedMBps;
            count++;
            positiveIndex++;
        }

        if (count > 0 || result.Count < targetPoints)
            result.Add(count > 0 ? sum / count : 0);

        return result.Where(v => v > 0).Take(targetPoints).ToList();
    }

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

    private static List<bool> DownsampleStalls(List<SpeedSample> writeSamples, List<SpeedSample> readSamples, int targetPoints)
    {
        // Combine write and read samples, preserving order.
        // IMPORTANT: Do NOT flag the write→read phase boundary as a stall.
        // Phase transitions naturally have a gap; only flag samples explicitly
        // marked as stalled by the I/O monitor.
        var allSamples = writeSamples.Concat(readSamples).ToList();
        if (allSamples.Count == 0) return new List<bool>();

        // If any sample in a bucket is explicitly stalled, mark the bucket as stalled.
        // We do NOT infer stalls from speed=0 at phase boundaries.
        var bucketSize = Math.Max(1, allSamples.Count / targetPoints);
        var result = new List<bool>(targetPoints);
        for (var i = 0; i < targetPoints && i * bucketSize < allSamples.Count; i++)
        {
            var start = i * bucketSize;
            var end = Math.Min(start + bucketSize, allSamples.Count);
            var stalled = false;
            for (var j = start; j < end; j++)
            {
                // Only count explicitly flagged stalls, not phase transitions
                if (allSamples[j].IsStalled)
                {
                    stalled = true;
                    break;
                }
            }
            result.Add(stalled);
        }
        return result;
    }

    private static (List<double> Write, List<double> Read) GetProfilePointsForChart(DiskCertificate cert)
    {
        var write = cert.WriteProfilePoints?.Where(v => v > 0).ToList() ?? new List<double>();
        var read = cert.ReadProfilePoints?.Where(v => v > 0).ToList() ?? new List<double>();

        // Older certificates and some absolute-destructive-test certificates were
        // persisted before chart profile points were stored. In those cases the DB
        // still contains aggregate speeds (Avg/Max, and often sanitize pass 1/2
        // averages), so render an approximate profile instead of showing
        // "Data nejsou k dispozici". This avoids forcing the user to repeat a very
        // long destructive surface write just to regenerate graph points.
        if (write.Count == 0)
        {
            write = BuildFallbackProfile(
                cert.Sanitize1AvgWriteMBps,
                cert.Sanitize2AvgWriteMBps,
                cert.AvgWriteSpeed,
                cert.MaxWriteSpeed);
        }

        if (read.Count == 0)
        {
            read = BuildFallbackProfile(
                cert.Sanitize1AvgReadMBps,
                cert.Sanitize2AvgReadMBps,
                cert.AvgReadSpeed,
                cert.MaxReadSpeed);
        }

        return (write, read);
    }

    private static List<double> BuildFallbackProfile(double? firstPass, double? secondPass, double average, double maximum)
    {
        var values = new List<double>();

        if (firstPass.GetValueOrDefault() > 0)
            values.Add(firstPass!.Value);
        if (secondPass.GetValueOrDefault() > 0)
            values.Add(secondPass!.Value);

        if (values.Count == 0 && average > 0)
        {
            if (maximum > average)
            {
                // Build a small, conservative synthetic curve from aggregates.
                values.Add(average);
                values.Add(maximum);
                values.Add(average);
            }
            else
            {
                values.Add(average);
                values.Add(average);
            }
        }

        if (values.Count == 1)
            values.Add(values[0]);

        return values;
    }

    private static string FormatSpeedForPdf(double value, string notAvailableText)
    {
        return value > 0
            ? value.ToString("F1", CultureInfo.InvariantCulture)
            : notAvailableText;
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
        return RenderCertificateJpegAsync(certificate, CancellationToken.None);
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

        // Speed consistency penalty
        if (session.AverageWriteSpeedMBps > 0 && session.MaxWriteSpeedMBps > 0)
        {
            var consistency = session.AverageWriteSpeedMBps / session.MaxWriteSpeedMBps;
            if (consistency < 0.7) score -= (0.7 - consistency) * 50;
        }

        // Catastrophic speed drop penalty: if min speed is < 20% of max, the disk has serious issues
        if (session.MaxWriteSpeedMBps > 0 && session.MinWriteSpeedMBps > 0)
        {
            var minToMaxRatio = session.MinWriteSpeedMBps / session.MaxWriteSpeedMBps;
            if (minToMaxRatio < 0.2) score -= (0.2 - minToMaxRatio) * 80;
        }
        if (session.MaxReadSpeedMBps > 0 && session.MinReadSpeedMBps > 0)
        {
            var minToMaxRatio = session.MinReadSpeedMBps / session.MaxReadSpeedMBps;
            if (minToMaxRatio < 0.2) score -= (0.2 - minToMaxRatio) * 80;
        }

        // Disk-type-aware speed expectations based on SMART and capacity
        var expectedSpeed = EstimateExpectedSpeed(session);
        if (expectedSpeed > 0 && session.AverageWriteSpeedMBps > 0)
        {
            var speedRatio = session.AverageWriteSpeedMBps / expectedSpeed;
            if (speedRatio < 0.5) score -= (0.5 - speedRatio) * 60;
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

    /// <summary>
    /// Estimates expected write speed (MB/s) based on disk type, SMART data, and capacity.
    /// Used to avoid penalizing old/small disks for their naturally low speeds.
    /// </summary>
    private static double EstimateExpectedSpeed(TestSession session)
    {
        // Determine disk type from SMART or capacity heuristics
        var isSsd = false;
        var isNvme = false;

        if (session.SmartBefore != null)
        {
            var deviceType = session.SmartBefore.DeviceType ?? string.Empty;
            if (deviceType.Contains("nvme", StringComparison.OrdinalIgnoreCase))
                isNvme = true;
            else if (session.SmartBefore.AvailableSpare.HasValue ||
                     session.SmartBefore.PercentageUsed.HasValue ||
                     session.SmartBefore.WearLevelingCount.HasValue)
                isSsd = true;
        }

        // If no SMART, use capacity heuristics: SSDs are typically 120GB+, very old HDDs < 80GB
        var capacityGB = session.SmartBefore != null ? 0 : 0; // We need diskCard capacity here, not available in session alone
        // Use persisted session average write speed to detect SSD-like behavior.
        // This avoids scanning the (now downsampled) sample collection and works
        // even when only a small representative subset is in memory.
        if (!isSsd && !isNvme && session.AverageWriteSpeedMBps > 150)
        {
            isSsd = true; // HDDs rarely sustain >150 MB/s
        }

        if (isNvme) return 1500;  // NVMe expected: ~1.5 GB/s
        if (isSsd) return 350;    // SATA SSD expected: ~350 MB/s

        // HDD: expected speed depends on age indicators
        var powerOnHours = session.SmartBefore?.PowerOnHours ?? 0;
        if (powerOnHours > 40000) return 60;   // Very old HDD
        if (powerOnHours > 20000) return 80;   // Older HDD
        return 100;                             // Modern HDD
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

    private string GenerateRecommendation(TestSession session, string effectiveGrade, double effectiveScore, bool recommended)
    {
        var hasCriticalSignals = HasCriticalRetirementSignals(session, effectiveGrade, effectiveScore);
        if (hasCriticalSignals)
        {
            return _locale?.GetString(
                "CertificatePdf.Recommendation.Retire",
                "Disk NENÍ vhodný k dalšímu používání. Doporučení: okamžitě vyřadit z provozu, nepoužívat pro žádná data a případná důležitá data obnovovat pouze ze záloh. Test sice mohl technicky doběhnout, ale výsledná známka/SMART/stally ukazují na kritickou degradaci média.")
                ?? "Disk NENÍ vhodný k dalšímu používání. Doporučení: okamžitě vyřadit z provozu, nepoužívat pro žádná data a případná důležitá data obnovovat pouze ze záloh. Test sice mohl technicky doběhnout, ale výsledná známka/SMART/stally ukazují na kritickou degradaci média.";
        }

        if (!recommended)
        {
            return _locale?.GetString(
                "CertificatePdf.Recommendation.NotRecommended",
                "Disk není doporučen k běžnému používání. Doporučujeme jej nenasazovat pro důležitá data, provést zálohu a zvážit výměnu. Výsledek testu nebo hodnocení neodpovídá bezpečnému provozu.")
                ?? "Disk není doporučen k běžnému používání. Doporučujeme jej nenasazovat pro důležitá data, provést zálohu a zvážit výměnu. Výsledek testu nebo hodnocení neodpovídá bezpečnému provozu.";
        }

        if (HasWarningSmartSignals(session))
        {
            return _locale?.GetString(
                "CertificatePdf.Recommendation.Worn",
                "Disk vykazuje známky opotřebení. Doporučujeme jej používat pouze s pravidelnou zálohou, sledovat SMART a při jakémkoli zhoršení jej vyměnit.")
                ?? "Disk vykazuje známky opotřebení. Doporučujeme jej používat pouze s pravidelnou zálohou, sledovat SMART a při jakémkoli zhoršení jej vyměnit.";
        }

        if (session.Errors.Count > 0 || session.WriteErrors > 0 || session.ReadErrors > 0 || session.VerificationErrors > 0)
        {
            return _locale?.GetString(
                "CertificatePdf.Recommendation.Errors",
                "Během testu byly detekovány chyby. Disk nepovažujte za plně důvěryhodný, dokud nebude problém vysvětlen opakovaným testem a kontrolou SMART.")
                ?? "Během testu byly detekovány chyby. Disk nepovažujte za plně důvěryhodný, dokud nebude problém vysvětlen opakovaným testem a kontrolou SMART.";
        }

        return _locale?.GetString(
            "CertificatePdf.Recommendation.Good",
            "Disk je v dobrém stavu a lze jej bezpečně používat.")
            ?? "Disk je v dobrém stavu a lze jej bezpečně používat.";
    }

    private static bool IsRecommendedForUse(TestSession session, string effectiveGrade, double effectiveScore)
    {
        return session.Result == TestResult.Pass
               && !HasCriticalRetirementSignals(session, effectiveGrade, effectiveScore)
               && !string.Equals(effectiveGrade, "D", StringComparison.OrdinalIgnoreCase);
    }


    private static string SelectWorseGrade(string? sessionGrade, string calculatedGrade)
    {
        if (string.IsNullOrWhiteSpace(sessionGrade) || sessionGrade == "?")
        {
            return calculatedGrade;
        }

        return GradeRank(sessionGrade) >= GradeRank(calculatedGrade)
            ? sessionGrade.Trim().ToUpperInvariant()
            : calculatedGrade;
    }

    private static int GradeRank(string? grade)
    {
        return string.IsNullOrWhiteSpace(grade) ? 6 : char.ToUpperInvariant(grade.Trim()[0]) switch
        {
            'A' => 1,
            'B' => 2,
            'C' => 3,
            'D' => 4,
            'E' => 5,
            'F' => 6,
            _ => 6
        };
    }

    private static bool HasCriticalRetirementSignals(TestSession session, string effectiveGrade, double effectiveScore)
    {
        if (string.Equals(effectiveGrade, "E", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effectiveGrade, "F", StringComparison.OrdinalIgnoreCase) ||
            effectiveScore < 40 ||
            session.HealthAssessment == HealthAssessment.Critical ||
            session.Result == TestResult.Fail)
        {
            return true;
        }

        if (session.SmartBefore?.IsFailing == true ||
            session.SmartBefore?.IsHealthy == false ||
            session.SmartAfter?.IsFailing == true ||
            session.SmartAfter?.IsHealthy == false)
        {
            return true;
        }

        if (session.SmartBefore?.UncorrectableErrorCount is > 0 ||
            session.SmartBefore?.MediaErrors is > 0 ||
            session.SmartAfter?.UncorrectableErrorCount is > 0 ||
            session.SmartAfter?.MediaErrors is > 0)
        {
            return true;
        }

        if (session.SmartChanges.Any(c => IsCriticalSmartAttribute(c.AttributeId) && c.Change > 0))
        {
            return true;
        }

        if (SmartDelta(session.SmartBefore?.ReallocatedSectorCount, session.SmartAfter?.ReallocatedSectorCount) > 0 ||
            SmartDelta(session.SmartBefore?.PendingSectorCount, session.SmartAfter?.PendingSectorCount) > 0 ||
            SmartDelta(session.SmartBefore?.UncorrectableErrorCount, session.SmartAfter?.UncorrectableErrorCount) > 0 ||
            SmartDelta(session.SmartBefore?.MediaErrors, session.SmartAfter?.MediaErrors) > 0)
        {
            return true;
        }

        // Use the database-level stall count (StalledSampleCount) when available,
        // which is set by CertificateExportService from GetSpeedSampleStallInfoAsync.
        // Fall back to scanning the in-memory samples (now downsampled) only when
        // StalledSampleCount was not populated (e.g. older code paths).
        var stallCount = session.StalledSampleCount > 0
            ? session.StalledSampleCount
            : session.WriteSamples.Count(s => s.IsStalled) + session.ReadSamples.Count(s => s.IsStalled);
        if (stallCount >= 3)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(session.Notes) &&
            (session.Notes.Contains("krit", StringComparison.OrdinalIgnoreCase) ||
             session.Notes.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
             session.Notes.Contains("failing", StringComparison.OrdinalIgnoreCase) ||
             session.Notes.Contains("vyřad", StringComparison.OrdinalIgnoreCase) ||
             session.Notes.Contains("vyrad", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static bool HasWarningSmartSignals(TestSession session)
    {
        return session.SmartBefore?.ReallocatedSectorCount is > 0 ||
               session.SmartBefore?.PendingSectorCount is > 0 ||
               session.SmartAfter?.ReallocatedSectorCount is > 0 ||
               session.SmartAfter?.PendingSectorCount is > 0 ||
               session.SmartChanges.Any(c => IsCriticalSmartAttribute(c.AttributeId));
    }

    private static int SmartDelta(int? before, int? after) => Math.Max(0, (after ?? 0) - (before ?? 0));

    private static bool IsCriticalSmartAttribute(int attributeId) => attributeId is 5 or 187 or 197 or 198 or 199;
    private static string ResolveDisplaySerial(string? smartSerial, string? storedSerial)
    {
        if (!string.IsNullOrWhiteSpace(smartSerial)) return smartSerial;
        if (!string.IsNullOrWhiteSpace(storedSerial)) return storedSerial;
        return "-";
    }

    private static void PopulateAdvancedTestMetrics(DiskCertificate certificate, TestSession session)
    {
        TryPopulateSeekMetrics(certificate, session);
        TryPopulateSanitizationComparisonMetrics(certificate, session);
    }

    private static void TryPopulateSeekMetrics(DiskCertificate certificate, TestSession session)
    {
        if (string.IsNullOrWhiteSpace(session.SeekResultsJson))
            return;

        List<SeekTestResult> seekResults;
        try
        {
            var singleResult = JsonSerializer.Deserialize<SeekTestResult>(session.SeekResultsJson);
            if (singleResult?.IsCompleted == true || singleResult?.SeekCount > 0 || singleResult?.AverageLatencyMs > 0)
            {
                seekResults = new List<SeekTestResult> { singleResult };
            }
            else
            {
                var envelope = JsonSerializer.Deserialize<SeekResultsEnvelope>(session.SeekResultsJson);
                seekResults = new List<SeekTestResult>();
                if (envelope?.FullStroke != null) seekResults.Add(envelope.FullStroke);
                if (envelope?.Random != null) seekResults.Add(envelope.Random);
                if (envelope?.Skip != null) seekResults.Add(envelope.Skip);
            }
        }
        catch (JsonException)
        {
            return;
        }

        seekResults = seekResults
            .Where(r => r != null && (r.SeekCount > 0 || r.AverageLatencyMs > 0 || r.Samples.Count > 0))
            .ToList();
        if (seekResults.Count == 0)
            return;

        var successfulSamples = seekResults
            .SelectMany(r => r.Samples)
            .Where(s => !s.HasError && s.LatencyMs > 0)
            .OrderBy(s => s.Index)
            .ToList();
        var latencies = successfulSamples.Select(s => s.LatencyMs).OrderBy(v => v).ToList();

        if (latencies.Count > 0)
        {
            var avg = latencies.Average();
            certificate.SeekAvgLatencyMs ??= avg;
            certificate.SeekMinLatencyMs ??= latencies[0];
            certificate.SeekMaxLatencyMs ??= latencies[^1];
            certificate.SeekStdDevLatencyMs ??= Math.Sqrt(latencies.Average(v => Math.Pow(v - avg, 2)));
            certificate.SeekMedianLatencyMs ??= PercentileSorted(latencies, 0.50);
            certificate.SeekP95LatencyMs ??= PercentileSorted(latencies, 0.95);
            certificate.SeekP99LatencyMs ??= PercentileSorted(latencies, 0.99);
            certificate.SeekLatencyPoints = DownsampleSpeeds(successfulSamples.Select(s => s.LatencyMs), MaxChartPoints);
        }
        else
        {
            var averageLatencies = seekResults.Select(r => r.AverageLatencyMs).Where(v => v > 0).ToList();
            if (averageLatencies.Count > 0)
            {
                certificate.SeekAvgLatencyMs ??= averageLatencies.Average();
                certificate.SeekMinLatencyMs ??= seekResults.Where(r => r.MinLatencyMs > 0).Select(r => r.MinLatencyMs).DefaultIfEmpty(0).Min();
                certificate.SeekMaxLatencyMs ??= seekResults.Where(r => r.MaxLatencyMs > 0).Select(r => r.MaxLatencyMs).DefaultIfEmpty(0).Max();
                certificate.SeekStdDevLatencyMs ??= Math.Sqrt(averageLatencies.Average(v => Math.Pow(v - certificate.SeekAvgLatencyMs.Value, 2)));
                certificate.SeekMedianLatencyMs ??= seekResults.Where(r => r.MedianLatencyMs > 0).Select(r => r.MedianLatencyMs).DefaultIfEmpty(certificate.SeekAvgLatencyMs.Value).Average();
                certificate.SeekP95LatencyMs ??= seekResults.Where(r => r.P95LatencyMs > 0).Select(r => r.P95LatencyMs).DefaultIfEmpty(certificate.SeekAvgLatencyMs.Value).Max();
                certificate.SeekP99LatencyMs ??= seekResults.Where(r => r.P99LatencyMs > 0).Select(r => r.P99LatencyMs).DefaultIfEmpty(certificate.SeekP95LatencyMs ?? certificate.SeekAvgLatencyMs.Value).Max();
            }
        }

        certificate.SeekTotalCount ??= seekResults.Sum(r => Math.Max(r.SeekCount, r.Samples.Count));
        certificate.SeekErrorCount ??= seekResults.Sum(r => Math.Max(0, r.ErrorCount));
        certificate.SeekTypeSummaries = seekResults
            .Select(r => BuildSeekTypeSummary(r))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (string.IsNullOrWhiteSpace(certificate.SeekTestSummary) && certificate.SeekTypeSummaries.Count > 0)
            certificate.SeekTestSummary = string.Join(" | ", certificate.SeekTypeSummaries);

        if (certificate.ErrorCount <= 0)
            certificate.ErrorCount = certificate.SeekErrorCount ?? 0;
    }
    private static void TryPopulateSanitizationComparisonMetrics(DiskCertificate certificate, TestSession session)
    {
        if (string.IsNullOrWhiteSpace(session.Sanitize1ResultJson) && string.IsNullOrWhiteSpace(session.Sanitize2ResultJson))
            return;

        SanitizationResult? sanitize1 = null;
        SanitizationResult? sanitize2 = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(session.Sanitize1ResultJson))
                sanitize1 = JsonSerializer.Deserialize<SanitizationResult>(session.Sanitize1ResultJson);
            if (!string.IsNullOrWhiteSpace(session.Sanitize2ResultJson))
                sanitize2 = JsonSerializer.Deserialize<SanitizationResult>(session.Sanitize2ResultJson);
        }
        catch (JsonException)
        {
            return;
        }

        if (sanitize1 == null && sanitize2 == null)
            return;

        certificate.SanitizationPerformed = true;
        certificate.SanitizationMethod ??= "Zero-fill + verify (2×)";

        if (sanitize1 != null)
        {
            certificate.Sanitize1AvgWriteMBps ??= sanitize1.WriteSpeedMBps;
            certificate.Sanitize1AvgReadMBps ??= sanitize1.ReadSpeedMBps;
            certificate.Sanitize1Errors ??= sanitize1.ErrorsDetected;
        }

        if (sanitize2 != null)
        {
            certificate.Sanitize2AvgWriteMBps ??= sanitize2.WriteSpeedMBps;
            certificate.Sanitize2AvgReadMBps ??= sanitize2.ReadSpeedMBps;
            certificate.Sanitize2Errors ??= sanitize2.ErrorsDetected;
            certificate.DataVerified = sanitize2.Success;
            certificate.FileSystem ??= sanitize2.FileSystem;
            certificate.VolumeLabel ??= sanitize2.VolumeLabel;
        }

        if (certificate.Sanitize1AvgWriteMBps > 0 && certificate.Sanitize2AvgWriteMBps > 0)
        {
            certificate.WriteSpeedChangePercent ??= (certificate.Sanitize2AvgWriteMBps.Value - certificate.Sanitize1AvgWriteMBps.Value)
                / certificate.Sanitize1AvgWriteMBps.Value * 100;
        }

        if (certificate.Sanitize1AvgReadMBps > 0 && certificate.Sanitize2AvgReadMBps > 0)
        {
            certificate.ReadSpeedChangePercent ??= (certificate.Sanitize2AvgReadMBps.Value - certificate.Sanitize1AvgReadMBps.Value)
                / certificate.Sanitize1AvgReadMBps.Value * 100;
        }

        var finalSanitize = sanitize2 ?? sanitize1;
        if (finalSanitize != null)
        {
            if (certificate.AvgWriteSpeed <= 0)
                certificate.AvgWriteSpeed = finalSanitize.WriteSpeedMBps;
            if (certificate.MaxWriteSpeed <= 0)
                certificate.MaxWriteSpeed = finalSanitize.WriteSpeedMBps;
            if (certificate.AvgReadSpeed <= 0)
                certificate.AvgReadSpeed = finalSanitize.ReadSpeedMBps;
            if (certificate.MaxReadSpeed <= 0)
                certificate.MaxReadSpeed = finalSanitize.ReadSpeedMBps;
        }

        if (certificate.ErrorCount <= 0)
        {
            certificate.ErrorCount = Math.Max(0, certificate.Sanitize1Errors ?? 0) + Math.Max(0, certificate.Sanitize2Errors ?? 0);
        }
    }

    private sealed class SeekResultsEnvelope
    {
        public SeekTestResult? FullStroke { get; set; }
        public SeekTestResult? Random { get; set; }
        public SeekTestResult? Skip { get; set; }
    }

    private static string BuildSeekTypeSummary(SeekTestResult r)
    {
        var samples = r.Samples.Where(s => !s.HasError && s.LatencyMs > 0).Select(s => s.LatencyMs).OrderBy(v => v).ToList();
        var min = FirstPositive(r.MinLatencyMs, samples.Count > 0 ? samples[0] : 0);
        var avg = FirstPositive(r.AverageLatencyMs, samples.Count > 0 ? samples.Average() : 0);
        var max = FirstPositive(r.MaxLatencyMs, samples.Count > 0 ? samples[^1] : 0);
        var median = FirstPositive(r.MedianLatencyMs, samples.Count > 0 ? PercentileSorted(samples, 0.50) : 0);
        var p95 = FirstPositive(r.P95LatencyMs, samples.Count > 0 ? PercentileSorted(samples, 0.95) : 0);
        var p99 = FirstPositive(r.P99LatencyMs, samples.Count > 0 ? PercentileSorted(samples, 0.99) : 0);
        var std = FirstPositive(r.LatencyStdDevMs, samples.Count > 0 ? Math.Sqrt(samples.Average(v => Math.Pow(v - avg, 2))) : 0);
        var count = Math.Max(r.SeekCount, samples.Count);
        return $"{r.TestType}: seeks {count}, min {min:F2} ms, avg {avg:F2} ms, max {max:F2} ms, median {median:F2} ms, P95 {p95:F2} ms, P99 {p99:F2} ms, σ {std:F2} ms, errors {r.ErrorCount}";
    }

    private static double PercentileSorted(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        var index = (int)Math.Clamp(Math.Ceiling(sortedValues.Count * percentile) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    private static double FirstPositive(params double[] values)
    {
        foreach (var value in values)
        {
            if (value > 0) return value;
        }
        return 0;
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
        using var font = ResolveFont(14, bold: false);
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
        TryAssignOwnershipToSudoUser(filePath);
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

            using var titleFont = ResolveFont(24, bold: true);
            using var valueFont = ResolveFont(14, bold: false);
            using var gradeFont = ResolveFont(64, bold: true);
            using var smallFont = ResolveFont(14, bold: false);
            using var chartLabelFont = ResolveFont(13, bold: false);

            using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var accentPaint = new SKPaint { Color = new SKColor(15, 76, 129), IsAntialias = true };
            using var borderPaint = new SKPaint { Color = new SKColor(210, 215, 223), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };

            var gradeColor = GetGradeColor(certificate.Grade);
            using var gradePaint = new SKPaint { Color = gradeColor, IsAntialias = true };
            using var gradeStrokePaint = new SKPaint { Color = gradeColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f };

            // Border
            canvas.DrawRect(10, 10, labelWidth - 20, labelHeight - 20, borderPaint);

            // Title
            DrawText(canvas, _locale?.GetString("CertificatePdf.Label.Title", "CERTIFIKÁT KVALITY DISKU") ?? "CERTIFIKÁT KVALITY DISKU", 20, 20, titleFont, accentPaint);

            // Info
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.Label.Model", "Model: {0}") ?? "Model: {0}", certificate.DiskModel), 20, 60, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.Label.SerialNumber", "S/N: {0}") ?? "S/N: {0}", certificate.SerialNumber), 20, 84, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.Label.Capacity", "Kapacita: {0}") ?? "Kapacita: {0}", certificate.Capacity), 20, 108, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.Label.GradeScore", "Známka: {0}  |  Skóre: {1:F0}/100") ?? "Známka: {0}  |  Skóre: {1:F0}/100", certificate.Grade, certificate.Score), 20, 132, valueFont, textPaint);
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.Label.Date", "Datum: {0}") ?? "Datum: {0}", certificate.GeneratedAt.ToString("dd.MM.yyyy HH:mm")), 20, 156, valueFont, textPaint);

            // Grade seal
            float sealX = labelWidth - 160;
            float sealY = 40;
            float sealSize = 100f;

            var gradeText = certificate.Grade;
            float gradeTextWidth = gradeFont.MeasureText(gradeText);
            float gradeX = sealX + (sealSize - gradeTextWidth) / 2f;
            // Center vertically: DrawText adds font.Size, so we subtract it here
            float gradeY = sealY + (sealSize - gradeFont.Size) / 2f;
            DrawText(canvas, gradeText, gradeX, gradeY, gradeFont, gradePaint);

            // Footer
            DrawText(canvas, string.Format(_locale?.GetString("CertificatePdf.Label.Footer", "DiskChecker v1.0 | {0}") ?? "DiskChecker v1.0 | {0}", certificate.CertificateNumber), 20, labelHeight - 30, smallFont, textPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            using var fs = File.OpenWrite(filePath);
            data.SaveTo(fs);
            TryAssignOwnershipToSudoUser(filePath);

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
