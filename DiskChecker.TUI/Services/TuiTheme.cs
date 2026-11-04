using Spectre.Console;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Terminal palette mirroring the desktop application's semantic colours.
/// Every UI surface uses these helpers so grades, health scores and temperatures
/// keep exactly the same meaning in both front ends.
/// </summary>
public static class TuiTheme
{
    public static readonly Color Primary = Color.FromHex("#58A6FF");
    public static readonly Color Success = Color.FromHex("#3FB950");
    public static readonly Color Warning = Color.FromHex("#D29922");
    public static readonly Color Danger = Color.FromHex("#F85149");
    public static readonly Color Muted = Color.FromHex("#8B949E");
    public static readonly Color Text = Color.FromHex("#F0F6FC");
    public static readonly Color WriteSeries = Color.FromHex("#EF4444");
    public static readonly Color ReadSeries = Color.FromHex("#22C55E");
    public static readonly Color Accent = Color.FromHex("#BC8CFF");
    public static readonly Color ChartGuide = Color.FromHex("#4B5563");

    /// <summary>Grade colour bands identical to <c>DiskStatusCardItem.GradeColor</c>.</summary>
    public static Color GradeColor(string? grade) => grade?.Trim().ToUpperInvariant() switch
    {
        "A" => Color.FromHex("#27AE60"),
        "B" => Color.FromHex("#2ECC71"),
        "C" => Color.FromHex("#F39C12"),
        "D" => Color.FromHex("#E67E22"),
        "E" => Color.FromHex("#E74C3C"),
        "F" => Color.FromHex("#C0392B"),
        _ => Color.FromHex("#6C757D"),
    };

    /// <summary>Health score colour bands identical to the desktop UI.</summary>
    public static Color HealthScoreColor(double? score)
    {
        if (score is null)
        {
            return Color.FromHex("#888888");
        }

        double s = score.Value;
        if (s >= 90) return Color.FromHex("#27AE60");
        if (s >= 80) return Color.FromHex("#2ECC71");
        if (s >= 70) return Color.FromHex("#F1C40F");
        if (s >= 60) return Color.FromHex("#E67E22");
        if (s >= 50) return Color.FromHex("#E74C3C");
        return Color.FromHex("#C0392B");
    }

    /// <summary>Temperature colour bands identical to the desktop UI.</summary>
    public static Color TemperatureColor(int? celsius)
    {
        if (celsius is null or <= 0)
        {
            return Muted;
        }

        return celsius.Value switch
        {
            < 40 => Color.FromHex("#27AE60"),
            < 50 => Color.FromHex("#2ECC71"),
            < 60 => Color.FromHex("#F1C40F"),
            < 70 => Color.FromHex("#E67E22"),
            _ => Color.FromHex("#E74C3C"),
        };
    }

    public static string GradeMarkup(string? grade) =>
        $"[{GradeColor(grade).ToMarkup()}]{Markup.Escape(grade ?? "N/A")}[/]";

    public static string TemperatureMarkup(int? celsius) =>
        celsius is null or <= 0
            ? "[grey]N/A[/]"
            : $"[{TemperatureColor(celsius).ToMarkup()}]{celsius.Value}°C[/]";

    public static string ScoreMarkup(double? score) =>
        score is null
            ? "[grey]N/A[/]"
            : $"[{HealthScoreColor(score).ToMarkup()}]{score.Value:F0}[/]";
}
