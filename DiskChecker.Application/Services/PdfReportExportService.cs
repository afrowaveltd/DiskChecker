using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using SkiaSharp;

namespace DiskChecker.Application.Services;

/// <summary>
/// Generates PDF exports for reports.
/// </summary>
public class PdfReportExportService : IPdfReportExporter
{
    private const float PageWidth = 595f;
    private const float PageHeight = 842f;
    private const float Margin = 40f;
    private readonly ITestReportExporter _exporter;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfReportExportService"/> class.
    /// </summary>
    /// <param name="exporter">Report exporter.</param>
    public PdfReportExportService(ITestReportExporter exporter)
    {
        _exporter = exporter;
    }

    /// <inheritdoc />
    public byte[] GenerateCertificatePdf(TestReportData report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(report.SmartCheck);

        using var stream = new MemoryStream();
        using var document = SKDocument.CreatePdf(stream);
        using var canvas = document.BeginPage(PageWidth, PageHeight);

        DrawCertificate(canvas, report);

        document.EndPage();
        document.Close();

        return stream.ToArray();
    }

    private void DrawCertificate(SKCanvas canvas, TestReportData report)
    {
        var smart = report.SmartCheck;
        var surface = report.SurfaceTest;

        using var headerFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 20);
        using var smallFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 10);
        using var tableFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 11);
        using var gradeFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 72);

        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var grayPaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
        using var gradePaint = new SKPaint { Color = SKColors.SteelBlue, IsAntialias = true };

        var y = Margin + 20;
        canvas.DrawText("Certifikát kvality disku", Margin, y, SKTextAlign.Left, headerFont, textPaint);

        var model = smart.SmartaData.DeviceModel ?? smart.SmartaData.ModelFamily ?? "Unknown";
        canvas.DrawText(model, PageWidth - Margin - 150, Margin + 16, SKTextAlign.Left, smallFont, grayPaint);
        canvas.DrawText(model, PageWidth - Margin - 150, Margin + 30, SKTextAlign.Left, smallFont, grayPaint);

        y += 40;
        canvas.DrawText(smart.Rating.Grade.ToString(), Margin, y + 72, SKTextAlign.Left, gradeFont, gradePaint);

        var tableX = Margin + 140;
        var tableY = y + 20;
        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Model", smart.SmartaData.DeviceModel ?? "Unknown");
        tableY += 18;
        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Serial", smart.SmartaData.SerialNumber ?? "Unknown");
        tableY += 18;
        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Firmware", smart.SmartaData.FirmwareVersion ?? "Unknown");
        tableY += 18;
        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Power On Hours", smart.SmartaData.PowerOnHours.ToString());
        tableY += 18;
        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Reallocated", smart.SmartaData.ReallocatedSectorCount.ToString());
        tableY += 18;
        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Pending", smart.SmartaData.PendingSectorCount.ToString());
        tableY += 18;
        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Uncorrectable", smart.SmartaData.UncorrectableErrorCount.ToString());
        tableY += 18;
        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Temperature", $"{smart.SmartaData.Temperature:F1} °C");
        if (smart.SmartaData.WearLevelingCount.HasValue)
        {
            tableY += 18;
            DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Wear Leveling", $"{smart.SmartaData.WearLevelingCount.Value}%");
        }

        y += 180;
        canvas.DrawText("Výkon při testu", Margin, y, SKTextAlign.Left, headerFont, textPaint);
        y += 10;

        DrawSpeedChart(canvas, surface, new SKRect(Margin, y + 20, PageWidth - Margin, y + 180));

        canvas.DrawText($"Datum testu: {smart.TestDate:dd. MM. yyyy HH:mm}", Margin, PageHeight - Margin, SKTextAlign.Left, smallFont, grayPaint);
    }

    private static void DrawTableRow(SKCanvas canvas, SKFont font, SKPaint paint, float x, float y, string label, string value)
    {
        canvas.DrawText(label, x, y, SKTextAlign.Left, font, paint);
        canvas.DrawText(value, x + 140, y, SKTextAlign.Left, font, paint);
    }

    private static void DrawSpeedChart(SKCanvas canvas, SurfaceTestResult? surface, SKRect rect)
    {
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.LightGray, StrokeWidth = 1 };
        canvas.DrawRect(rect, borderPaint);

        if (surface == null || surface.Samples.Count < 2)
        {
            using var textFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI"), 12);
            using var textPaint = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
            canvas.DrawText("Graf není k dispozici.", rect.Left + 10, rect.MidY, SKTextAlign.Left, textFont, textPaint);
            return;
        }

        var max = surface.Samples.Max(sample => sample.ThroughputMbps);
        if (max <= 0)
        {
            return;
        }

        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.SteelBlue,
            StrokeWidth = 2,
            IsAntialias = true
        };

        var step = rect.Width / (surface.Samples.Count - 1);
        using var path = new SKPath();
        for (var i = 0; i < surface.Samples.Count; i++)
        {
            var x = rect.Left + step * i;
            var y = rect.Bottom - (float)(surface.Samples[i].ThroughputMbps / max) * rect.Height;
            if (i == 0)
            {
                path.MoveTo(x, y);
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        canvas.DrawPath(path, linePaint);
    }
}
