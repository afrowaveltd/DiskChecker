using System.Globalization;
using DiskChecker.Core.Models;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Formatting helpers shared by every terminal screen. The rules mirror the
/// desktop application so numbers and units look identical in both front ends.
/// </summary>
public static class TuiFormats
{
    private const double KiB = 1024d;
    private const double MiB = 1024d * 1024d;
    private const double GiB = 1024d * 1024d * 1024d;
    private const double TiB = 1024d * 1024d * 1024d * 1024d;

    /// <summary>Binary human readable size with two decimals, e.g. "512.00 GB".</summary>
    public static string Bytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        double value = bytes;
        if (value >= TiB) return Formattable(value / TiB, "F2", "TB");
        if (value >= GiB) return Formattable(value / GiB, "F2", "GB");
        if (value >= MiB) return Formattable(value / MiB, "F2", "MB");
        if (value >= KiB) return Formattable(value / KiB, "F1", "KB");
        return $"{bytes} B";
    }

    public static string Bytes(ulong bytes) => Bytes((long)Math.Min(bytes, long.MaxValue));

    /// <summary>Single decimal size used by disk lists, e.g. "465.8 GB" / "1.8 TB".</summary>
    public static string Capacity(long bytes)
    {
        if (bytes <= 0)
        {
            return "Unknown";
        }

        double gb = bytes / GiB;
        return gb >= 1000 ? Formattable(gb / 1024, "F1", "TB") : Formattable(gb, "F1", "GB");
    }

    public static string Capacity(ulong bytes) => Capacity((long)Math.Min(bytes, long.MaxValue));

    /// <summary>Decimal size used by the backup workflow (1 kB = 1000 B).</summary>
    public static string DecimalSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes >= 1_000_000_000_000) return Formattable(bytes / 1_000_000_000_000d, "F2", "TB");
        if (bytes >= 1_000_000_000) return Formattable(bytes / 1_000_000_000d, "F2", "GB");
        if (bytes >= 1_000_000) return Formattable(bytes / 1_000_000d, "F2", "MB");
        if (bytes >= 1_000) return Formattable(bytes / 1_000d, "F1", "KB");
        return $"{bytes} B";
    }

    /// <summary>HH:mm:ss duration.</summary>
    public static string Duration(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours:D2}:{value.Minutes:D2}:{value.Seconds:D2}");
    }

    /// <summary>mm:ss duration used by gauges and short tests.</summary>
    public static string ShortDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return string.Create(CultureInfo.InvariantCulture, $"{value.Minutes:D2}:{value.Seconds:D2}");
    }

    /// <summary>Estimated remaining time: "mm:ss" or "HH:mm:ss", em dash when unknown.</summary>
    public static string Eta(TimeSpan? remaining)
    {
        if (remaining is null) return "—";
        var value = remaining.Value;
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1 ? Duration(value) : ShortDuration(value);
    }

    public static string Speed(double megabytesPerSecond) =>
        double.IsNaN(megabytesPerSecond) || double.IsInfinity(megabytesPerSecond)
            ? "—"
            : Formattable(megabytesPerSecond, "F1", "MB/s");

    public static string SpeedNoUnit(double megabytesPerSecond) =>
        double.IsNaN(megabytesPerSecond) || double.IsInfinity(megabytesPerSecond)
            ? "—"
            : megabytesPerSecond.ToString("F1", CultureInfo.InvariantCulture);

    public static string Latency(double milliseconds) =>
        double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)
            ? "—"
            : $"{milliseconds.ToString("F2", CultureInfo.InvariantCulture)} ms";

    public static string Percent(double value, string format = "F1") =>
        value.ToString(format, CultureInfo.InvariantCulture) + "%";

    public static string Temperature(int? celsius) => celsius is null or <= 0 ? "N/A" : $"{celsius.Value}°C";

    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Grade letter from a 0-100 score using the shared thresholds.</summary>
    public static string GradeFromScore(double score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 55 => "D",
        >= 40 => "E",
        _ => "F",
    };

    /// <summary>Healthy / warning / critical classification used by the dashboard.</summary>
    public static string HealthStatus(string? grade) => grade?.Trim().ToUpperInvariant() switch
    {
        "A" or "B" => "OK",
        "C" => "Warning",
        "D" or "E" or "F" => "Critical",
        _ => "Unknown",
    };

    public static bool IsHealthy(string? grade) =>
        grade is not null && (grade.Equals("A", StringComparison.OrdinalIgnoreCase)
            || grade.Equals("B", StringComparison.OrdinalIgnoreCase)
            || grade.Equals("C", StringComparison.OrdinalIgnoreCase));

    public static string BusType(CoreBusType busType) => busType == CoreBusType.Unknown
        ? "SATA"
        : busType.GetDisplayName();

    public static string BusTypeDisplay(CoreDriveInfo drive)
    {
        var model = drive.Name ?? string.Empty;
        if (model.Contains("nvme", StringComparison.OrdinalIgnoreCase)) return "NVMe";
        if (model.Contains("usb", StringComparison.OrdinalIgnoreCase)) return "USB";
        return BusType(drive.BusType);
    }

    /// <summary>Textual progress bar, e.g. "████████░░░░ 62 %".</summary>
    public static string Bar(double percent, int width = 28)
    {
        if (width <= 0) return string.Empty;
        double clamped = Math.Clamp(percent, 0, 100) / 100d;
        int filled = (int)Math.Round(clamped * width);
        return new string('█', filled) + new string('░', width - filled);
    }

    /// <summary>Truncates text to a terminal friendly width.</summary>
    public static string Ellipsis(string? text, int width)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (width <= 0) return string.Empty;
        if (text.Length <= width) return text;
        return width <= 1 ? text[..width] : text[..(width - 1)] + "…";
    }

    private static string Formattable(double value, string format, string unit) =>
        $"{value.ToString(format, CultureInfo.InvariantCulture)} {unit}";
}
