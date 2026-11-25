using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using SkiaSharp;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// A single A4 certificate page shared by read-only, surface, seek and combined
/// diagnostics. Text and diagrams are drawn as vector content in PDF. Chart
/// series are bounded before entering this renderer; no raw telemetry is loaded.
/// </summary>
internal sealed class CertificatePdfPageRenderer
{
    private readonly CertificateReportText report;
    private CertificatePdfPageRenderer(CertificateReportText? text) => report = text ?? CertificateReportText.Czech;
    private static string L(CertificateReportText? text, string key) => (text ?? CertificateReportText.Czech).Get(key);
    private static string F(CertificateReportText? text, string key, params object?[] args) => (text ?? CertificateReportText.Czech).Format(key, args);
    private const float W = 1240f;
    private const float H = 1754f;
    private static readonly SKColor Ink = new(26, 43, 63);
    private static readonly SKColor Muted = new(92, 107, 124);
    private static readonly SKColor Blue = new(20, 72, 122);
    private static readonly SKColor Green = new(8, 126, 99);
    private static readonly SKColor Red = new(187, 55, 62);
    private static readonly SKColor Border = new(215, 225, 235);
    private static readonly SKColor Panel = new(247, 249, 252);

    internal static byte[] CreatePdf(DiskCertificate main, CertificateReportText? report = null)
    {
        var renderer = new CertificatePdfPageRenderer(report);
        using var stream = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(stream)
            ?? throw new InvalidOperationException("SkiaSharp PDF engine is unavailable."))
        {
            var canvas = pdf.BeginPage(595f, 842f);
            canvas.Save();
            canvas.Scale(595f / W);
            renderer.Draw(canvas, main);
            canvas.Restore();
            pdf.EndPage();
            pdf.Close();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// One document for 2-3 distinct tests of the same disk. The first page is
    /// an overview, followed by a complete original A4 certificate for EACH
    /// selected test. No scores or grades are merged or silently dropped.
    /// </summary>
    internal static byte[] CreateBundlePdf(IReadOnlyList<DiskCertificate> certificates, CertificateReportText? report = null)
    {
        var renderer = new CertificatePdfPageRenderer(report);
        CombinedCertificatePolicy.Validate(certificates);
        using var stream = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(stream)
            ?? throw new InvalidOperationException("SkiaSharp PDF engine is unavailable."))
        {
            var cover = pdf.BeginPage(595f, 842f);
            cover.Save();
            cover.Scale(595f / W);
            renderer.DrawBundleCover(cover, certificates);
            cover.Restore();
            pdf.EndPage();

            foreach (var certificate in certificates)
            {
                var page = pdf.BeginPage(595f, 842f);
                page.Save();
                page.Scale(595f / W);
                renderer.Draw(page, certificate);
                page.Restore();
                pdf.EndPage();
            }
            pdf.Close();
        }
        return stream.ToArray();
    }

    /// <summary>
    /// The report cover must display the time the TEST took place, not the
    /// later time at which a legacy certificate was reconstructed for export.
    /// </summary>
    internal static DateTime BundleTestDate(DiskCertificate certificate) =>
        certificate.TestSession?.StartedAt is DateTime started && started != default
            ? started
            : certificate.GeneratedAt;

    private void DrawBundleCover(SKCanvas canvas, IReadOnlyList<DiskCertificate> items)
    {
        canvas.Clear(SKColors.White);
        using var large = Font(37, true);
        using var title = Font(24, true);
        using var body = Font(19);
        using var small = Font(16);
        using var tiny = Font(14);

        var disk = items[0];
        Text(canvas, "DISKCHECKER", 50, 76, large, Blue);
        Text(canvas, L(report, "CertificateReport.BundleTitle"), 50, 112, title, Ink);
        // The top of the summary uses the SAME disk search barcode.
        DrawBarcodeHeader(canvas, disk, tiny, small);

        Box(canvas, 48, 165, 1144, 170, Panel);
        Text(canvas, L(report, "CertificateReport.Device"), 67, 203, title, Blue);
        Text(canvas, ClipHeaderText(disk.DiskModel, body, 870), 67, 252, body, Ink);
        Text(canvas, $"S/N: {disk.SerialNumber}", 67, 299, small, Ink);

        Text(canvas, L(report, "CertificateReport.SelectedTests"), 67, 379, title, Blue);
        Text(canvas, L(report, "CertificateReport.SeparateTests"),
            67, 414, small, Muted);

        for (int index = 0; index < items.Count; index++)
        {
            var cert = items[index];
            float y = 452 + index * 310f;
            Box(canvas, 48, y, 1144, 288, Panel);
            Text(canvas, $"{index + 1}. {cert.TestType}", 68, y + 47, title, Blue);
            Text(canvas, F(report, "CertificateReport.GradeScore", cert.Grade, cert.Score),
                68, y + 102, body, GradeColor(cert.Grade));
            Text(canvas, F(report, "CertificateReport.CertificateNumber", ClipHeaderText(cert.CertificateNumber, small, 760)),
                68, y + 146, small, Ink);
            Text(canvas, F(report, "CertificateReport.BundleDateDuration", report.Date(BundleTestDate(cert)), cert.TestDuration.ToString(@"hh\:mm\:ss")),
                68, y + 185, small, Ink);
            Text(canvas, F(report, "CertificateReport.ErrorsRead", cert.ErrorCount, SpeedValue(cert.AvgReadSpeed, cert.SanitizationPerformed, report)),
                68, y + 225, small, Ink);
            Text(canvas, F(report, "CertificateReport.PageDetails", index + 2), 68, y + 263, small, Muted);
        }

        Text(canvas, F(report, "CertificateReport.PartsPages", items.Count, items.Count + 1), 68, 1498, small, Ink);
        Box(canvas, 48, 1530, 1144, 172, SKColors.White);
        Text(canvas, L(report, "CertificateReport.Scope"), 68, 1570, title, Blue);
        Wrapped(canvas, L(report, "CertificateReport.BundleScope"), 68, 1615, 1090, small, Ink, 3);
    }

    internal static byte[] CreatePreview(DiskCertificate main, CertificateReportText? report = null)
    {
        var renderer = new CertificatePdfPageRenderer(report);
        using var surface = SKSurface.Create(new SKImageInfo((int)W, (int)H))
            ?? throw new InvalidOperationException("Cannot allocate certificate preview.");
        renderer.Draw(surface.Canvas, main);
        using var image = surface.Snapshot();
        using var bytes = image.Encode(SKEncodedImageFormat.Jpeg, 88);
        return bytes.ToArray();
    }

    private static SKFont Font(float size, bool bold = false)
    {
        // The same embedded fonts as the original certificate avoid differing
        // glyphs when exporting on a headless Linux server.
        string resource = bold
            ? "DiskChecker.Infrastructure.Resources.DejaVuSans-Bold.ttf"
            : "DiskChecker.Infrastructure.Resources.DejaVuSans.ttf";
        try
        {
            using Stream? data = typeof(CertificatePdfPageRenderer).Assembly.GetManifestResourceStream(resource);
            if (data != null)
            {
                using var copy = new MemoryStream();
                data.CopyTo(copy);
                var typeface = SKTypeface.FromStream(new MemoryStream(copy.ToArray()));
                if (typeface != null)
                {
                    return new SKFont(typeface, size);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            // Use installed fonts when an embedded resource cannot be loaded.
        }

        return new SKFont(SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default, size);
    }

    private static SKPaint Paint(SKColor color, SKPaintStyle style = SKPaintStyle.Fill, float strokeWidth = 1f) =>
        new() { Color = color, IsAntialias = true, Style = style, StrokeWidth = strokeWidth };

    private static void Box(SKCanvas c, float x, float y, float w, float h, SKColor background, float radius = 12f)
    {
        using var fill = Paint(background);
        using var line = Paint(Border, SKPaintStyle.Stroke, 1.5f);
        c.DrawRoundRect(x, y, w, h, radius, radius, fill);
        c.DrawRoundRect(x, y, w, h, radius, radius, line);
    }

    private static void Text(SKCanvas c, string? value, float x, float baseline, SKFont font, SKColor color)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        using var paint = Paint(color);
        c.DrawText(Clean(value), x, baseline, font, paint);
    }

    private static string Clean(string value) => value
        .Replace("✅", "[OK]", StringComparison.Ordinal)
        .Replace("⚠️", "[!]", StringComparison.Ordinal)
        .Replace("⚠", "[!]", StringComparison.Ordinal)
        .Replace("❌", "[X]", StringComparison.Ordinal)
        .Replace("\r", "", StringComparison.Ordinal);

    /// <summary>Draws only what fits; any omitted content gets a visible continuation marker.</summary>
    private int Wrapped(SKCanvas c, string? value, float x, float y, float width,
        SKFont font, SKColor color, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(value) || maxLines <= 0) return 0;

        var words = Clean(value).Replace("\n", " \n ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>(maxLines + 1);
        string current = "";
        foreach (string word in words)
        {
            if (word == "\n")
            {
                if (current.Length > 0) lines.Add(current);
                current = "";
                continue;
            }

            string trial = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(trial) > width && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = trial;
            }
        }

        if (current.Length > 0) lines.Add(current);
        bool truncated = lines.Count > maxLines;
        int count = Math.Min(lines.Count, maxLines);
        for (int i = 0; i < count; i++)
        {
            string line = lines[i];
            if (truncated && i == maxLines - 1)
            {
                string suffix = L(report, "CertificateReport.MoreInHistory");
                while (line.Length > 0 && font.MeasureText(line + suffix) > width)
                    line = line[..^1];
                line += suffix;
            }

            Text(c, line, x, y + i * (font.Size + 9f), font, color);
        }

        return count;
    }

    private void Draw(SKCanvas c, DiskCertificate primary)
    {
        c.Clear(SKColors.White);
        using var large = Font(37, true);
        using var title = Font(24, true);
        using var body = Font(19);
        using var small = Font(16);
        using var tiny = Font(14);
        using var gradeFont = Font(70, true);

        DiskCertificate? paired = primary.PairedCertificate;
        bool combined = paired != null;
        Text(c, "DISKCHECKER", 50, 76, large, Blue);
        Text(c, combined ? L(report, "CertificateReport.PairedTitle") : L(report, "CertificateReport.Title"),
            50, 112, title, Ink);
        DrawBarcodeHeader(c, primary, tiny, small);

        Box(c, 48, 140, 1144, 186, Panel);
        Text(c, L(report, "CertificateReport.Device"), 67, 173, title, Blue);
        Wrapped(c, primary.DiskModel, 67, 209, 650, body, Ink, 2);
        Text(c, $"S/N: {primary.SerialNumber}", 67, 274, body, Ink);
        Text(c, F(report, "CertificateReport.CapacityType", primary.Capacity, primary.DiskType), 650, 215, small, Ink);
        Text(c, F(report, "CertificateReport.InterfaceFirmware", Display(primary.Interface), Display(primary.Firmware)), 650, 249, small, Ink);
        Text(c, F(report, "CertificateReport.Test", primary.TestType) + (combined ? $" + {paired!.TestType}" : ""), 650, 284, small, Muted);

        Box(c, 48, 344, 1144, 147, SKColors.White);
        Text(c, L(report, "CertificateReport.SmartTitle"), 67, 379, title, Blue);
        bool primarySmartObserved = HasSmartData(primary);
        var smart = primarySmartObserved || paired == null ? primary : paired;
        bool smartObserved = HasSmartData(smart);
        Text(c, $"SMART: {(smartObserved ? (smart.SmartPassed ? L(report, "CertificateReport.SmartOkay") : L(report, "CertificateReport.SmartWarning")) : L(report, "CertificateReport.SmartUnavailable"))}",
            67, 417, body, smartObserved && !smart.SmartPassed ? Red : Ink);
        Text(c, F(report, "CertificateReport.Hours", smartObserved ? NullableNumber(smart.PowerOnHours, report) : "N/A"), 67, 455, small, Ink);
        Text(c, F(report, "CertificateReport.Starts", smartObserved ? NullableNumber(smart.PowerCycles, report) : "N/A"), 332, 455, small, Ink);
        Text(c, F(report, "CertificateReport.Reallocated", smartObserved ? smart.ReallocatedSectors.ToString() : "N/A"), 617, 417, body, Ink);
        Text(c, F(report, "CertificateReport.Pending", smartObserved ? smart.PendingSectors.ToString() : "N/A"), 909, 417, body, Ink);
        Text(c, L(report, "CertificateReport.SmartCaveat"), 617, 462, small, Muted);

        float testWidth = combined ? 558f : 1144f;
        DrawTest(c, primary, 48, 511, testWidth, 1014f, large, title, body, small, tiny, gradeFont);
        if (combined)
        {
            DrawTest(c, paired!, 634, 511, testWidth, 1014f, large, title, body, small, tiny, gradeFont);
        }

        Box(c, 48, 1542, 1144, 160, Panel);
        Text(c, combined ? L(report, "CertificateReport.PairedScope") : L(report, "CertificateReport.Scope"), 68, 1580, title, Blue);
        string footer = combined
            ? L(report, "CertificateReport.PairedFooter")
            : L(report, "CertificateReport.SingleFooter");
        Wrapped(c, footer, 68, 1614, 1100, small, Ink, 2);
        Wrapped(c, ScopeMethodologyNote(primary, paired, report),
            68, 1670, 1100, tiny, Muted, 2);
    }

    /// <summary>
    /// Explain why the sanitization I/O curve may be faster than the whole-phase
    /// averages. Reading verifies zero-filled buffers in memory between I/O calls;
    /// deliberately scheduled write pauses are excluded from write throughput.
    /// </summary>
    internal static string ScopeMethodologyNote(DiskCertificate primary, DiskCertificate? paired = null, CertificateReportText? report = null) =>
        primary.SanitizationPerformed || paired?.SanitizationPerformed == true
            ? L(report, "CertificateReport.SanitizeMethodology")
            : L(report, "CertificateReport.OtherMethodology");

    private static string SpeedValue(double value, bool sanitization, CertificateReportText? report = null) =>
        value > 0 ? (report ?? CertificateReportText.Czech).Number(value, "F1") + (sanitization ? " MiB/s" : " MB/s") : "N/A";

    internal static string ReadAverageText(DiskCertificate cert, CertificateReportText? report = null) =>
        cert.SanitizationPerformed
            ? F(report, "CertificateReport.ReadVerifyAverage", SpeedValue(cert.AvgReadSpeed, true, report))
            : F(report, "CertificateReport.ReadAverage", SpeedValue(cert.AvgReadSpeed, false, report));

    internal static string WriteAverageText(DiskCertificate cert, CertificateReportText? report = null)
    {
        var value = cert.IsReadOnlyTest ? L(report, "CertificateReport.NoWrite") : SpeedValue(cert.AvgWriteSpeed, cert.SanitizationPerformed, report);
        return cert.SanitizationPerformed
            ? F(report, "CertificateReport.WriteActiveAverage", value)
            : F(report, "CertificateReport.WriteAverage", value);
    }

    internal static string SpeedChartTitle(DiskCertificate cert, CertificateReportText? report = null) =>
        cert.SanitizationPerformed
            ? L(report, "CertificateReport.SanitizeChart")
            : L(report, "CertificateReport.SpeedChart");

    /// <summary>
    /// Keep the header inside the physical printable area even for long IDs.
    /// The barcode always identifies a disk, never a particular certificate.
    /// </summary>
    private void DrawBarcodeHeader(SKCanvas c, DiskCertificate cert, SKFont tiny, SKFont small)
    {
        const float x = 850f;
        const float right = 1185f;
        string? barcode = DiskBarcode.ForCertificate(cert);
        if (barcode != null)
        {
            Code128Barcode.Draw(c, barcode, x, 35f, right - x, 45f);
            Text(c, barcode, x + 93f, 96f, tiny, Ink);
        }

        Text(c, ClipHeaderText($"ID: {cert.CertificateNumber}", small, right - x),
            x, barcode == null ? 76f : 116f, small, Muted);
        Text(c, F(report, "CertificateReport.Issued", report.Date(cert.GeneratedAt)),
            x, barcode == null ? 102f : 136f, tiny, Muted);
    }

    private static string ClipHeaderText(string value, SKFont font, float availableWidth)
    {
        if (font.MeasureText(value) <= availableWidth)
            return value;

        const string suffix = "…";
        while (value.Length > 0 && font.MeasureText(value + suffix) > availableWidth)
            value = value[..^1];
        return value + suffix;
    }

    private static string NullableNumber(int n, CertificateReportText report) => n > 0 ? report.Number(n, "N0") : "N/A";

    private static string Display(string? text) => string.IsNullOrWhiteSpace(text) ? "N/A" : text;

    private static bool HasSmartData(DiskCertificate c) =>
        c.PowerOnHours > 0 || c.PowerCycles > 0 || c.SmartAttributes.Count > 0
        || c.ReallocatedSectors > 0 || c.PendingSectors > 0;

    private static bool Seek(DiskCertificate cert) =>
        cert.SeekAvgLatencyMs.HasValue ||
        cert.TestType.Contains("Seek", StringComparison.OrdinalIgnoreCase) ||
        cert.SeekLatencyPoints.Count > 0;

    // A localized human-readable TestType must not change certificate
    // classification. Combined seek + sanitization is a comprehensive test,
    // even when its display label is Czech or a different language.
    private static bool IsComprehensive(DiskCertificate cert) =>
        !cert.IsReadOnlyTest &&
        (string.Equals(cert.TestType, "AbsoluteDestructive", StringComparison.OrdinalIgnoreCase) ||
         (cert.SanitizationPerformed && Seek(cert)));

    /// <summary>True only for seek-only results, never for a completed sanitization.</summary>
    internal static bool IsSeekOnly(DiskCertificate cert) =>
        Seek(cert) && !IsComprehensive(cert) &&
        !cert.SanitizationPerformed && !cert.IsReadOnlyTest;

    /// <summary>Describes the actual recorded test category, not its localized UI label.</summary>
    internal static string Name(DiskCertificate cert, CertificateReportText? report = null)
    {
        if (IsComprehensive(cert)) return L(report, "CertificateReport.Comprehensive");
        if (cert.IsReadOnlyTest) return L(report, "CertificateReport.ReadOnly");
        if (IsSeekOnly(cert)) return L(report, "CertificateReport.Seek");
        if (cert.SanitizationPerformed) return L(report, "CertificateReport.Sanitization");
        if (cert.TestType.Contains("SMART", StringComparison.OrdinalIgnoreCase)) return L(report, "CertificateReport.SmartDiagnostic");
        return L(report, "CertificateReport.Surface");
    }

    private void DrawTest(SKCanvas c, DiskCertificate cert,
        float x, float y, float width, float height, SKFont large, SKFont title,
        SKFont body, SKFont small, SKFont tiny, SKFont gradeFont)
    {
        Box(c, x, y, width, height, Panel);
        float left = x + 20;
        float available = width - 40;
        Text(c, Name(cert, report), left, y + 41, title, Blue);
        Text(c, F(report, "CertificateReport.GradeScore", cert.Grade, cert.Score),
            left, y + 91, title, GradeColor(cert.Grade));
        Text(c, F(report, "CertificateReport.TestDuration", cert.TestType, cert.TestDuration.ToString(@"hh\:mm\:ss")), left, y + 128, small, Ink);
        Text(c, F(report, "CertificateReport.ErrorsTemp", cert.ErrorCount, Display(cert.TemperatureRange)), left, y + 160, small, Ink);

        bool seekOnly = IsSeekOnly(cert);
        if (seekOnly)
        {
            Text(c, F(report, "CertificateReport.SeekAverage", Ms(cert.SeekAvgLatencyMs), Ms(cert.SeekP95LatencyMs)),
                left, y + 200, small, Ink);
            Text(c, F(report, "CertificateReport.SeekCount", Ms(cert.SeekP99LatencyMs), cert.SeekTotalCount?.ToString() ?? "N/A"),
                left, y + 233, small, Ink);
        }
        else
        {
            Text(c, ReadAverageText(cert, report), left, y + 200, small, Ink);
            Text(c, WriteAverageText(cert, report), left, y + 233, small, Ink);
        }

        float chartY = y + 255;
        float chartHeight = 380;
        bool hasSeek = Seek(cert);
        bool hasSpeeds = cert.ReadProfilePoints.Count > 0 || cert.WriteProfilePoints.Count > 0
            || cert.Sanitize1ReadPoints.Count > 0 || cert.Sanitize1WritePoints.Count > 0;
        if (hasSeek && hasSpeeds)
        {
            float half = (available - 14) / 2f;
            DrawSpeedChart(c, cert, left, chartY, half, chartHeight, tiny);
            DrawSeekChart(c, cert, left + half + 14, chartY, half, chartHeight, tiny);
        }
        else if (hasSeek)
        {
            DrawSeekChart(c, cert, left, chartY, available, chartHeight, tiny);
        }
        else if (hasSpeeds)
        {
            DrawSpeedChart(c, cert, left, chartY, available, chartHeight, tiny);
        }
        else
        {
            Box(c, left, chartY, available, chartHeight, SKColors.White, 6);
            Text(c, L(report, "CertificateReport.NoChart"), left + 20, chartY + 45, body, Muted);
        }

        // Allocate room by the actual number of rendered text lines rather than
        // leaving a permanently reserved blank band below a short seek comment.
        float notesTitleY = y + 674;
        float notesTextY = notesTitleY + 29;
        Text(c, L(report, "CertificateReport.Notes"), left, notesTitleY, small, Blue);
        int notesLines = Wrapped(c,
            string.IsNullOrWhiteSpace(cert.Notes) ? cert.SeekTestSummary ?? L(report, "CertificateReport.NoNotes") : cert.Notes,
            left, notesTextY, available, small, Ink, 4);

        float assessmentTitleY = Math.Max(y + 765,
            notesTextY + notesLines * (small.Size + 9f) + 18f);
        float assessmentTextY = assessmentTitleY + 30;
        Text(c, L(report, "CertificateReport.Assessment"), left, assessmentTitleY, small, Blue);
        int assessmentLines = Wrapped(c, string.IsNullOrWhiteSpace(cert.TechnicianAssessment)
                ? L(report, "CertificateReport.NoAssessment") : cert.TechnicianAssessment,
            left, assessmentTextY, available, small, Ink, 2);

        float recommendationTitleY = Math.Max(y + 876,
            assessmentTextY + assessmentLines * (small.Size + 9f) + 18f);
        Text(c, L(report, "CertificateReport.Recommendation"), left, recommendationTitleY, small, Blue);
        float recommendationTextY = recommendationTitleY + 28;
        int availableLines = recommendationTextY + 2 * (tiny.Size + 9f) < y + height - 12 ? 2 : 1;
        Wrapped(c, report.TranslateKnownRecommendation(cert.RecommendationNotes), left, recommendationTextY,
            available, tiny, Ink, availableLines);
    }

    private string Ms(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? report.Number(value.Value, "F2") + " ms" : "N/A";

    private static SKColor GradeColor(string? grade) => grade?.ToUpperInvariant() switch
    {
        "A" or "B" => Green,
        "C" or "D" => new SKColor(168, 105, 19),
        _ => Red
    };

    private void DrawSpeedChart(SKCanvas c, DiskCertificate cert, float x, float y,
        float width, float height, SKFont small)
    {
        Box(c, x, y, width, height, SKColors.White, 6);
        Text(c, SpeedChartTitle(cert, report), x + 15, y + 30, small, Blue);
        var series = new List<(IReadOnlyList<double> Points, SKColor Color, string Label)>();
        if (cert.Sanitize1WritePoints.Count > 0 || cert.Sanitize2WritePoints.Count > 0)
        {
            series.Add((cert.Sanitize1WritePoints, Red, "W1"));
            series.Add((cert.Sanitize1ReadPoints, Green, "R1"));
            series.Add((cert.Sanitize2WritePoints, new SKColor(118, 32, 54), "W2"));
            series.Add((cert.Sanitize2ReadPoints, new SKColor(23, 99, 145), "R2"));
        }
        else
        {
            if (!cert.IsReadOnlyTest && cert.WriteProfilePoints.Count > 0)
                series.Add((cert.WriteProfilePoints, Red, L(report, "CertificateReport.Write")));
            if (cert.ReadProfilePoints.Count > 0)
                series.Add((cert.ReadProfilePoints, Green, L(report, "CertificateReport.Read")));
        }

        float left = x + 40, top = y + 55, plotW = width - 60, plotH = height - 90;
        DrawAxes(c, left, top, plotW, plotH);
        double maximum = series.SelectMany(item => item.Points)
            .Where(v => double.IsFinite(v) && v > 0).DefaultIfEmpty(1d).Max();
        if (maximum <= 0) maximum = 1;
        foreach (var s in series)
        {
            DrawSeries(c, s.Points, left, top, plotW, plotH, maximum, s.Color);
        }
        Text(c, $"{maximum:F0}", x + 2, top + 10, small, Muted);
        Text(c, "0", x + 20, top + plotH + 4, small, Muted);
        Text(c, "0%", left, y + height - 14, small, Muted);
        Text(c, "100%", x + width - 72, y + height - 14, small, Muted);
        Text(c, string.Join(" / ", series.Select(s => s.Label)), x + width - 200, y + 30, small, Muted);
    }

    private void DrawSeekChart(SKCanvas c, DiskCertificate cert, float x, float y,
        float width, float height, SKFont small)
    {
        Box(c, x, y, width, height, SKColors.White, 6);
        Text(c, L(report, "CertificateReport.SeekChart"), x + 15, y + 30, small, Blue);
        float left = x + 45, top = y + 55, plotW = width - 65, plotH = height - 90;
        DrawAxes(c, left, top, plotW, plotH);
        var values = cert.SeekLatencyPoints.Where(v => double.IsFinite(v) && v >= 0).Take(1024).ToArray();
        if (values.Length == 0)
        {
            Text(c, L(report, "CertificateReport.NoSeekPoints"), left + 12, top + 65, small, Muted);
            return;
        }

        double max = Math.Max(values.Max(), cert.SeekP99LatencyMs ?? 0);
        if (max <= 0) max = 1;
        max *= 1.08;
        DrawSeries(c, values, left, top, plotW, plotH, max, Blue);
        if (cert.SeekP95LatencyMs is > 0 && cert.SeekP95LatencyMs.Value <= max)
        {
            float lineY = top + plotH * (1 - (float)(cert.SeekP95LatencyMs.Value / max));
            using var line = Paint(new SKColor(219, 134, 46), SKPaintStyle.Stroke, 2f);
            c.DrawLine(left, lineY, left + plotW, lineY, line);
        }
        Text(c, report.Number(max, "F1"), x + 4, top + 8, small, Muted);
        Text(c, "0", x + 24, top + plotH + 4, small, Muted);
        Text(c, SeekChartCoverageText(cert, values.Length, report), left, y + height - 14, small, Muted);
    }

    // Stored chart points may already be reduced and exclude failed operations.
    // Their ordinal positions are not the original operation indices.
    internal static string SeekChartCoverageText(DiskCertificate cert, int plottedCount, CertificateReportText? report = null) =>
        F(report, "CertificateReport.SeekCoverage", plottedCount, cert.SeekTotalCount?.ToString() ?? "N/A");

    private static void DrawAxes(SKCanvas c, float left, float top, float w, float h)
    {
        using var axis = Paint(Border, SKPaintStyle.Stroke, 1.2f);
        for (int i = 0; i <= 4; i++)
        {
            float yy = top + h * i / 4f;
            c.DrawLine(left, yy, left + w, yy, axis);
        }
        c.DrawLine(left, top, left, top + h, axis);
    }

    private static void DrawSeries(SKCanvas c, IReadOnlyList<double> points, float x, float y,
        float w, float h, double maximum, SKColor color)
    {
        if (points.Count == 0) return;
        using var stroke = Paint(color, SKPaintStyle.Stroke, 2.2f);
        using var path = new SKPath();
        bool started = false;
        for (int i = 0; i < points.Count; i++)
        {
            double value = points[i];
            if (!double.IsFinite(value) || value < 0)
            {
                started = false;
                continue;
            }

            float px = x + w * i / Math.Max(1, points.Count - 1);
            float py = y + h * (1 - Math.Clamp((float)(value / maximum), 0f, 1f));
            if (!started) path.MoveTo(px, py);
            else path.LineTo(px, py);
            started = true;
        }

        c.DrawPath(path, stroke);
    }
}
