using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>Produces bounded CSV from actual disk comparison results, with formula-safe text cells.</summary>
public static class DiskComparisonCsvExporter
{
    public static string Build(IEnumerable<DiskComparisonResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var rows = new List<string>
        {
            "Rank,DiskCardId,Model,Serial,Grade,Score,Write MBps,Read MBps,Errors,Recommendation"
        };
        foreach (var r in results)
        {
            var fields = new[]
            {
                r.Rank.ToString(CultureInfo.InvariantCulture),
                r.Disk.Id.ToString(CultureInfo.InvariantCulture),
                r.Disk.ModelName,
                r.Disk.SerialNumber,
                r.Grade,
                r.Score.ToString("0.###", CultureInfo.InvariantCulture),
                r.HasWriteMeasurement ? r.AvgWriteSpeed.ToString("0.###", CultureInfo.InvariantCulture) : "N/A",
                r.HasReadMeasurement ? r.AvgReadSpeed.ToString("0.###", CultureInfo.InvariantCulture) : "N/A",
                r.ErrorCount.ToString(CultureInfo.InvariantCulture),
                r.Recommendation
            };
            rows.Add(string.Join(",", fields.Select(Escape)));
        }
        return string.Join("\r\n", rows) + "\r\n";
    }

    private static string Escape(string? value)
    {
        var content = value ?? string.Empty;
        var trimmed = content.TrimStart(' ', '\t', '\r', '\n');
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]))
            content = "'" + content;
        return "\"" + content.Replace("\"", "\"\"") + "\"";
    }
}
