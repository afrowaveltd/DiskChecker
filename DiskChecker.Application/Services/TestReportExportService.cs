using System.Globalization;
using System.Text;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services;

/// <summary>
/// Exports SMART and surface test results into multiple formats.
/// </summary>
public class TestReportExportService : ITestReportExporter
{
    /// <inheritdoc />
    public string GenerateText(TestReportData report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(report.SmartCheck);

        var smart = report.SmartCheck;
        var surface = report.SurfaceTest;
        var sb = new StringBuilder();

        sb.AppendLine("DiskChecker - Report");
        sb.AppendLine($"Date: {smart.TestDate:dd. MM. yyyy HH:mm}");
        sb.AppendLine($"Drive: {smart.Drive.Name} ({smart.Drive.Path})");
        sb.AppendLine($"Model: {smart.SmartaData.DeviceModel ?? "Unknown"}");
        sb.AppendLine($"Serial: {smart.SmartaData.SerialNumber ?? "Unknown"}");
        sb.AppendLine();
        sb.AppendLine("SMART:");
        sb.AppendLine($"  PowerOnHours: {smart.SmartaData.PowerOnHours}");
        sb.AppendLine($"  Reallocated: {smart.SmartaData.ReallocatedSectorCount}");
        sb.AppendLine($"  Pending: {smart.SmartaData.PendingSectorCount}");
        sb.AppendLine($"  Uncorrectable: {smart.SmartaData.UncorrectableErrorCount}");
        sb.AppendLine($"  Temperature: {smart.SmartaData.Temperature:F1} °C");
        if (smart.SmartaData.WearLevelingCount.HasValue)
        {
            sb.AppendLine($"  WearLeveling: {smart.SmartaData.WearLevelingCount.Value}%");
        }

        sb.AppendLine();
        sb.AppendLine($"Grade: {smart.Rating.Grade} ({smart.Rating.Score:F1})");

        if (smart.Rating.Warnings.Count > 0)
        {
            sb.AppendLine("Warnings:");
            foreach (var warning in smart.Rating.Warnings)
            {
                sb.AppendLine($"  - {warning}");
            }
        }

        if (surface != null)
        {
            sb.AppendLine();
            sb.AppendLine("Surface Test:");
            sb.AppendLine($"  Profile: {surface.Profile}");
            sb.AppendLine($"  Operation: {surface.Operation}");
            sb.AppendLine($"  TotalBytes: {surface.TotalBytesTested}");
            sb.AppendLine($"  Avg: {surface.AverageSpeedMbps:F1} MB/s");
            sb.AppendLine($"  Max: {surface.PeakSpeedMbps:F1} MB/s");
            sb.AppendLine($"  Min: {surface.MinSpeedMbps:F1} MB/s");
            sb.AppendLine($"  Errors: {surface.ErrorCount}");
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public string GenerateHtml(TestReportData report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(report.SmartCheck);

        var smart = report.SmartCheck;
        var surface = report.SurfaceTest;

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"cs\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:20px;}table{border-collapse:collapse;width:100%;}td,th{border:1px solid #ccc;padding:6px;}h2{margin-top:20px;}");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<h1>DiskChecker Report</h1>");
        sb.AppendLine($"<p><strong>Date:</strong> {smart.TestDate:dd. MM. yyyy HH:mm}</p>");
        sb.AppendLine($"<p><strong>Drive:</strong> {smart.Drive.Name} ({smart.Drive.Path})</p>");

        sb.AppendLine("<h2>SMART</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><td>Model</td><td>{Escape(smart.SmartaData.DeviceModel ?? "Unknown")}</td></tr>");
        sb.AppendLine($"<tr><td>Serial</td><td>{Escape(smart.SmartaData.SerialNumber ?? "Unknown")}</td></tr>");
        sb.AppendLine($"<tr><td>PowerOnHours</td><td>{smart.SmartaData.PowerOnHours}</td></tr>");
        sb.AppendLine($"<tr><td>Reallocated</td><td>{smart.SmartaData.ReallocatedSectorCount}</td></tr>");
        sb.AppendLine($"<tr><td>Pending</td><td>{smart.SmartaData.PendingSectorCount}</td></tr>");
        sb.AppendLine($"<tr><td>Uncorrectable</td><td>{smart.SmartaData.UncorrectableErrorCount}</td></tr>");
        sb.AppendLine($"<tr><td>Temperature</td><td>{smart.SmartaData.Temperature:F1} °C</td></tr>");
        if (smart.SmartaData.WearLevelingCount.HasValue)
        {
            sb.AppendLine($"<tr><td>WearLeveling</td><td>{smart.SmartaData.WearLevelingCount.Value}%</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine($"<h2>Grade</h2><p><strong>{smart.Rating.Grade}</strong> ({smart.Rating.Score:F1})</p>");

        if (surface != null)
        {
            sb.AppendLine("<h2>Surface Test</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><td>Profile</td><td>{surface.Profile}</td></tr>");
            sb.AppendLine($"<tr><td>Operation</td><td>{surface.Operation}</td></tr>");
            sb.AppendLine($"<tr><td>TotalBytes</td><td>{surface.TotalBytesTested}</td></tr>");
            sb.AppendLine($"<tr><td>Avg</td><td>{surface.AverageSpeedMbps:F1} MB/s</td></tr>");
            sb.AppendLine($"<tr><td>Max</td><td>{surface.PeakSpeedMbps:F1} MB/s</td></tr>");
            sb.AppendLine($"<tr><td>Min</td><td>{surface.MinSpeedMbps:F1} MB/s</td></tr>");
            sb.AppendLine($"<tr><td>Errors</td><td>{surface.ErrorCount}</td></tr>");
            sb.AppendLine("</table>");
        }

        if (smart.Rating.Warnings.Count > 0)
        {
            sb.AppendLine("<h2>Warnings</h2>");
            sb.AppendLine("<ul>");
            foreach (var warning in smart.Rating.Warnings)
            {
                sb.AppendLine($"<li>{Escape(warning)}</li>");
            }
            sb.AppendLine("</ul>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <inheritdoc />
    public string GenerateCsv(TestReportData report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(report.SmartCheck);

        var smart = report.SmartCheck;
        var surface = report.SurfaceTest;
        var values = new List<string>
        {
            smart.TestDate.ToString("O", CultureInfo.InvariantCulture),
            EscapeCsv(smart.Drive.Name),
            EscapeCsv(smart.Drive.Path),
            EscapeCsv(smart.SmartaData.SerialNumber ?? string.Empty),
            smart.Rating.Grade.ToString(),
            smart.Rating.Score.ToString("F1", CultureInfo.InvariantCulture),
            smart.SmartaData.PowerOnHours.ToString(CultureInfo.InvariantCulture),
            smart.SmartaData.ReallocatedSectorCount.ToString(CultureInfo.InvariantCulture),
            smart.SmartaData.PendingSectorCount.ToString(CultureInfo.InvariantCulture),
            smart.SmartaData.UncorrectableErrorCount.ToString(CultureInfo.InvariantCulture),
            smart.SmartaData.Temperature.ToString("F1", CultureInfo.InvariantCulture)
        };

        values.Add(surface?.Profile.ToString() ?? string.Empty);
        values.Add(surface?.Operation.ToString() ?? string.Empty);
        values.Add(surface?.AverageSpeedMbps.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty);
        values.Add(surface?.PeakSpeedMbps.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty);
        values.Add(surface?.MinSpeedMbps.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty);
        values.Add(surface?.ErrorCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        var header = string.Join(",", new[]
        {
            "Date","DriveName","DrivePath","Serial","Grade","Score","PowerOnHours","Reallocated","Pending","Uncorrectable","Temperature","Profile","Operation","AvgSpeed","MaxSpeed","MinSpeed","Errors"
        });

        return header + Environment.NewLine + string.Join(",", values);
    }

    /// <inheritdoc />
    public string GenerateCertificateHtml(TestReportData report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(report.SmartCheck);

        var smart = report.SmartCheck;
        var surface = report.SurfaceTest;
        var grade = smart.Rating.Grade.ToString();
        var smartDescription = smart.SmartaData.DeviceModel ?? smart.SmartaData.ModelFamily ?? "Disk";

        var chart = BuildChartSvg(surface);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"cs\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine("<style>");
        sb.AppendLine("@page{size:A4;margin:20mm;}body{font-family:Segoe UI,Arial,sans-serif;color:#222;}h1{margin:0 0 8px 0;}table{border-collapse:collapse;width:100%;}td,th{border:1px solid #ccc;padding:6px;} .header{display:flex;justify-content:space-between;} .badge{font-size:72px;font-weight:700;color:#4A90E2;} .grid{display:grid;grid-template-columns:140px 1fr;gap:12px;align-items:start;} .chart{margin-top:16px;} .meta{font-size:12px;color:#666;} .right-head{text-align:right;font-size:14px;} ");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine("<div><h1>Certifikát kvality disku</h1><div class=\"meta\">DiskChecker</div></div>");
        sb.AppendLine($"<div class=\"right-head\"><div>{Escape(smartDescription)}</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"grid\">");
        sb.AppendLine($"<div class=\"badge\">{Escape(grade)}</div>");
        sb.AppendLine("<div>");
        sb.AppendLine("<table>");
        sb.AppendLine($"<tr><td>Model</td><td>{Escape(smart.SmartaData.DeviceModel ?? "Unknown")}</td></tr>");
        sb.AppendLine($"<tr><td>Serial</td><td>{Escape(smart.SmartaData.SerialNumber ?? "Unknown")}</td></tr>");
        sb.AppendLine($"<tr><td>Firmware</td><td>{Escape(smart.SmartaData.FirmwareVersion ?? "Unknown")}</td></tr>");
        sb.AppendLine($"<tr><td>Power On Hours</td><td>{smart.SmartaData.PowerOnHours}</td></tr>");
        sb.AppendLine($"<tr><td>Reallocated</td><td>{smart.SmartaData.ReallocatedSectorCount}</td></tr>");
        sb.AppendLine($"<tr><td>Pending</td><td>{smart.SmartaData.PendingSectorCount}</td></tr>");
        sb.AppendLine($"<tr><td>Uncorrectable</td><td>{smart.SmartaData.UncorrectableErrorCount}</td></tr>");
        sb.AppendLine($"<tr><td>Temperature</td><td>{smart.SmartaData.Temperature:F1} °C</td></tr>");
        if (smart.SmartaData.WearLevelingCount.HasValue)
        {
            sb.AppendLine($"<tr><td>Wear Leveling</td><td>{smart.SmartaData.WearLevelingCount.Value}%</td></tr>");
        }
        sb.AppendLine("</table>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"chart\">");
        sb.AppendLine("<h2>Výkon při testu</h2>");
        sb.AppendLine(chart);
        sb.AppendLine("</div>");
        sb.AppendLine($"<div class=\"meta\">Datum testu: {smart.TestDate:dd. MM. yyyy HH:mm}</div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string BuildChartSvg(SurfaceTestResult? surface)
    {
        if (surface == null || surface.Samples.Count < 2)
        {
            return "<div class=\"meta\">Graf není k dispozici.</div>";
        }

        var max = surface.Samples.Max(sample => sample.ThroughputMbps);
        if (max <= 0)
        {
            return "<div class=\"meta\">Graf není k dispozici.</div>";
        }

        const double padding = 5;
        var width = 100 - (padding * 2);
        var height = 100 - (padding * 2);
        var step = width / (surface.Samples.Count - 1);
        var points = new List<string>(surface.Samples.Count);

        for (var i = 0; i < surface.Samples.Count; i++)
        {
            var x = padding + step * i;
            var y = padding + (height - (surface.Samples[i].ThroughputMbps / max * height));
            points.Add($"{x.ToString("F2", CultureInfo.InvariantCulture)},{y.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        var chart = new StringBuilder();
        chart.AppendLine("<svg class=\"chart\" viewBox=\"0 0 100 100\" preserveAspectRatio=\"none\">");
        chart.AppendLine("<line x1=\"5\" y1=\"5\" x2=\"5\" y2=\"95\" stroke=\"#777\" stroke-width=\"0.5\" />");
        chart.AppendLine("<line x1=\"5\" y1=\"95\" x2=\"95\" y2=\"95\" stroke=\"#777\" stroke-width=\"0.5\" />");
        chart.AppendLine($"<polyline points=\"{string.Join(" ", points)}\" fill=\"none\" stroke=\"#4A90E2\" stroke-width=\"2\" />");
        chart.AppendLine("</svg>");
        return chart.ToString();
    }

    private static string Escape(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
