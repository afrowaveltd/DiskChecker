using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DiskChecker.Core.Models;
using SkiaSharp;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// A single A4 certificate page shared by read-only, surface, seek and combined
/// diagnostics. Text and diagrams are drawn as vector content in PDF. Chart
/// series are bounded before entering this renderer; no raw telemetry is loaded.
/// </summary>
internal static class CertificatePdfPageRenderer
{
    private const float W = 1240f;
    private const float H = 1754f;
    private static readonly SKColor Ink = new(26, 43, 63);
    private static readonly SKColor Muted = new(92, 107, 124);
    private static readonly SKColor Blue = new(20, 72, 122);
    private static readonly SKColor Green = new(8, 126, 99);
    private static readonly SKColor Red = new(187, 55, 62);
    private static readonly SKColor Border = new(215, 225, 235);
    private static readonly SKColor Panel = new(247, 249, 252);

    internal static byte[] CreatePdf(DiskCertificate main)
    {
        using var stream = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(stream)
            ?? throw new InvalidOperationException("SkiaSharp PDF engine is unavailable."))
        {
            var canvas = pdf.BeginPage(595f, 842f);
            canvas.Save();
            canvas.Scale(595f / W);
            Draw(canvas, main);
            canvas.Restore();
            pdf.EndPage();
            pdf.Close();
        }

        return stream.ToArray();
    }

    internal static byte[] CreatePreview(DiskCertificate main)
    {
        using var surface = SKSurface.Create(new SKImageInfo((int)W, (int)H))
            ?? throw new InvalidOperationException("Cannot allocate certificate preview.");
        Draw(surface.Canvas, main);
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
    private static int Wrapped(SKCanvas c, string? value, float x, float y, float width,
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
                const string suffix = " ... [více v historii testu]";
                while (line.Length > 0 && font.MeasureText(line + suffix) > width)
                    line = line[..^1];
                line += suffix;
            }

            Text(c, line, x, y + i * (font.Size + 9f), font, color);
        }

        return count;
    }

    private static void Draw(SKCanvas c, DiskCertificate primary)
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
        Text(c, combined ? "SPOLEČNÝ DIAGNOSTICKÝ CERTIFIKÁT" : "DIAGNOSTICKÝ CERTIFIKÁT DISKU",
            50, 112, title, Ink);
        Text(c, $"ID: {primary.CertificateNumber}   |   Vystaveno: {primary.GeneratedAt:dd.MM.yyyy HH:mm}",
            790, 77, small, Muted);

        Box(c, 48, 140, 1144, 186, Panel);
        Text(c, "IDENTIFIKACE ZAŘÍZENÍ", 67, 173, title, Blue);
        Wrapped(c, primary.DiskModel, 67, 209, 650, body, Ink, 2);
        Text(c, $"S/N: {primary.SerialNumber}", 67, 274, body, Ink);
        Text(c, $"Kapacita: {primary.Capacity}    Typ: {primary.DiskType}", 650, 215, small, Ink);
        Text(c, $"Rozhraní: {Display(primary.Interface)}    FW: {Display(primary.Firmware)}", 650, 249, small, Ink);
        Text(c, $"Test: {primary.TestType}" + (combined ? $" + {paired!.TestType}" : ""), 650, 284, small, Muted);

        Box(c, 48, 344, 1144, 147, SKColors.White);
        Text(c, "SMART / HARDWAROVÉ SIGNÁLY", 67, 379, title, Blue);
        bool primarySmartObserved = HasSmartData(primary);
        var smart = primarySmartObserved || paired == null ? primary : paired;
        bool smartObserved = HasSmartData(smart);
        Text(c, $"SMART: {(smartObserved ? (smart.SmartPassed ? "bez hlášeného selhání" : "varování") : "nedostupné")}",
            67, 417, body, smartObserved && !smart.SmartPassed ? Red : Ink);
        Text(c, $"Hodiny: {(smartObserved ? NullableNumber(smart.PowerOnHours) : "N/A")}", 67, 455, small, Ink);
        Text(c, $"Starty: {(smartObserved ? NullableNumber(smart.PowerCycles) : "N/A")}", 332, 455, small, Ink);
        Text(c, $"Realokace: {(smartObserved ? smart.ReallocatedSectors.ToString() : "N/A")}", 617, 417, body, Ink);
        Text(c, $"Pending: {(smartObserved ? smart.PendingSectors.ToString() : "N/A")}", 909, 417, body, Ink);
        Text(c, "SMART je doplňující diagnostika, nikoli záruka bezchybnosti média.", 617, 462, small, Muted);

        float testWidth = combined ? 558f : 1144f;
        DrawTest(c, primary, 48, 511, testWidth, 1014f, large, title, body, small, tiny, gradeFont);
        if (combined)
        {
            DrawTest(c, paired!, 634, 511, testWidth, 1014f, large, title, body, small, tiny, gradeFont);
        }

        Box(c, 48, 1542, 1144, 160, Panel);
        Text(c, combined ? "SOUHRN DVOJICE TESTŮ" : "ROZSAH A OMEZENÍ", 68, 1580, title, Blue);
        string footer = combined
            ? "Výsledky obou testů jsou samostatné; neslučujeme známky ani z nich neodvozujeme novou."
            : "Známka platí pouze pro uvedený typ testu a zaznamenané podmínky.";
        Wrapped(c, footer, 68, 1614, 1100, small, Ink, 2);
        Wrapped(c, "Rychlost zápisu se nehodnotí při testu čtení. Údaje označené N/A nebyly měřeny. " +
            "Detailní protokol, historická telemetrie a úplné komentáře zůstávají v DiskCheckeru.",
            68, 1670, 1100, tiny, Muted, 2);
    }

    private static string NullableNumber(int n) => n > 0 ? n.ToString("N0", CultureInfo.InvariantCulture) : "N/A";

    private static string Display(string? text) => string.IsNullOrWhiteSpace(text) ? "N/A" : text;

    private static bool HasSmartData(DiskCertificate c) =>
        c.PowerOnHours > 0 || c.PowerCycles > 0 || c.SmartAttributes.Count > 0
        || c.ReallocatedSectors > 0 || c.PendingSectors > 0;

    private static bool Seek(DiskCertificate cert) =>
        cert.SeekAvgLatencyMs.HasValue ||
        cert.TestType.Contains("Seek", StringComparison.OrdinalIgnoreCase) ||
        cert.SeekLatencyPoints.Count > 0;

    private static string Name(DiskCertificate cert)
    {
        if (Seek(cert) && cert.TestType != "AbsoluteDestructive") return "SEEK / LATENCE";
        if (cert.IsReadOnlyTest) return "ČTENÍ BEZ ZÁPISU";
        if (cert.TestType == "AbsoluteDestructive") return "KOMPLEXNÍ DESTRUKTIVNÍ TEST";
        if (cert.SanitizationPerformed) return "SANITIZACE / OVĚŘENÍ";
        if (cert.TestType.Contains("SMART", StringComparison.OrdinalIgnoreCase)) return "SMART DIAGNOSTIKA";
        return "POVRCH / VÝKON";
    }

    private static void DrawTest(SKCanvas c, DiskCertificate cert,
        float x, float y, float width, float height, SKFont large, SKFont title,
        SKFont body, SKFont small, SKFont tiny, SKFont gradeFont)
    {
        Box(c, x, y, width, height, Panel);
        float left = x + 20;
        float available = width - 40;
        Text(c, Name(cert), left, y + 41, title, Blue);
        Text(c, $"Známka: {cert.Grade}    Skóre: {cert.Score:F0}/100",
            left, y + 91, title, GradeColor(cert.Grade));
        Text(c, $"Test: {cert.TestType}    Čas: {cert.TestDuration.ToString(@"hh\:mm\:ss")}", left, y + 128, small, Ink);
        Text(c, $"Chyby: {cert.ErrorCount}    Teplota: {Display(cert.TemperatureRange)}", left, y + 160, small, Ink);

        bool seekOnly = Seek(cert) && cert.TestType != "AbsoluteDestructive";
        if (seekOnly)
        {
            Text(c, $"Seek průměr: {Ms(cert.SeekAvgLatencyMs)}    P95: {Ms(cert.SeekP95LatencyMs)}",
                left, y + 200, small, Ink);
            Text(c, $"P99: {Ms(cert.SeekP99LatencyMs)}    Počet: {cert.SeekTotalCount?.ToString() ?? "N/A"}",
                left, y + 233, small, Ink);
        }
        else
        {
            string read = cert.AvgReadSpeed > 0 ? $"{cert.AvgReadSpeed:F1} MB/s" : "N/A";
            string write = cert.IsReadOnlyTest ? "N/A (bez zápisu)"
                : cert.AvgWriteSpeed > 0 ? $"{cert.AvgWriteSpeed:F1} MB/s" : "N/A";
            Text(c, $"Čtení průměr: {read}", left, y + 200, small, Ink);
            Text(c, $"Zápis průměr: {write}", left, y + 233, small, Ink);
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
            Text(c, "Graf pro tento typ testu není k dispozici.", left + 20, chartY + 45, body, Muted);
        }

        // Allocate room by the actual number of rendered text lines rather than
        // leaving a permanently reserved blank band below a short seek comment.
        float notesTitleY = y + 674;
        float notesTextY = notesTitleY + 29;
        Text(c, "KOMENTÁŘ TESTU", left, notesTitleY, small, Blue);
        int notesLines = Wrapped(c,
            string.IsNullOrWhiteSpace(cert.Notes) ? cert.SeekTestSummary ?? "Bez poznámek." : cert.Notes,
            left, notesTextY, available, small, Ink, 4);

        float assessmentTitleY = Math.Max(y + 765,
            notesTextY + notesLines * (small.Size + 9f) + 18f);
        float assessmentTextY = assessmentTitleY + 30;
        Text(c, "TECHNICKÉ POSOUZENÍ", left, assessmentTitleY, small, Blue);
        int assessmentLines = Wrapped(c, string.IsNullOrWhiteSpace(cert.TechnicianAssessment)
                ? "Bez doplňujícího hodnocení technika." : cert.TechnicianAssessment,
            left, assessmentTextY, available, small, Ink, 2);

        float recommendationTitleY = Math.Max(y + 876,
            assessmentTextY + assessmentLines * (small.Size + 9f) + 18f);
        Text(c, "DOPORUČENÍ", left, recommendationTitleY, small, Blue);
        float recommendationTextY = recommendationTitleY + 28;
        int availableLines = recommendationTextY + 2 * (tiny.Size + 9f) < y + height - 12 ? 2 : 1;
        Wrapped(c, cert.RecommendationNotes, left, recommendationTextY,
            available, tiny, Ink, availableLines);
    }

    private static string Ms(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? $"{value.Value:F2} ms" : "N/A";

    private static SKColor GradeColor(string? grade) => grade?.ToUpperInvariant() switch
    {
        "A" or "B" => Green,
        "C" or "D" => new SKColor(168, 105, 19),
        _ => Red
    };

    private static void DrawSpeedChart(SKCanvas c, DiskCertificate cert, float x, float y,
        float width, float height, SKFont small)
    {
        Box(c, x, y, width, height, SKColors.White, 6);
        Text(c, "PRŮBĚH RYCHLOSTI (MB/s)", x + 15, y + 30, small, Blue);
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
                series.Add((cert.WriteProfilePoints, Red, "Zápis"));
            if (cert.ReadProfilePoints.Count > 0)
                series.Add((cert.ReadProfilePoints, Green, "Čtení"));
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

    private static void DrawSeekChart(SKCanvas c, DiskCertificate cert, float x, float y,
        float width, float height, SKFont small)
    {
        Box(c, x, y, width, height, SKColors.White, 6);
        Text(c, "LATENCE SEEK (ms)", x + 15, y + 30, small, Blue);
        float left = x + 45, top = y + 55, plotW = width - 65, plotH = height - 90;
        DrawAxes(c, left, top, plotW, plotH);
        var values = cert.SeekLatencyPoints.Where(v => double.IsFinite(v) && v >= 0).Take(1024).ToArray();
        if (values.Length == 0)
        {
            Text(c, "Uložené body latence nejsou dostupné.", left + 12, top + 65, small, Muted);
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
        Text(c, $"{max:F1}", x + 4, top + 8, small, Muted);
        Text(c, "0", x + 24, top + plotH + 4, small, Muted);
        Text(c, "1", left, y + height - 14, small, Muted);
        Text(c, $"{values.Length}", x + width - 75, y + height - 14, small, Muted);
    }

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
