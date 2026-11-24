using System.Globalization;
using System.Text.Json;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// Immutable per-export language snapshot, independent of the UI and thread culture.
/// The same JSON vocabulary is embedded for headless use by both front ends.
/// </summary>
internal sealed class CertificateReportText
{
    private static readonly Lazy<CertificateReportText> Cs = new(() => new("cs"));
    private static readonly Lazy<CertificateReportText> En = new(() => new("en"));
    private readonly Dictionary<string, string> _strings;
    private readonly CultureInfo _culture;

    public string Language { get; }
    public static CertificateReportText Czech => Cs.Value;

    private CertificateReportText(string language)
    {
        Language = language;
        _culture = CultureInfo.GetCultureInfo(language == "cs" ? "cs-CZ" : "en-GB");
        using var stream = typeof(CertificateReportText).Assembly.GetManifestResourceStream(
            $"DiskChecker.ReportLocales.{language}.json")
            ?? throw new InvalidOperationException($"Missing report locale: {language}");
        using var reader = new StreamReader(stream);
        _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
            ?? throw new InvalidOperationException($"Empty report locale: {language}");
    }

    public static CertificateReportText Resolve(string? preference, string? applicationLanguage)
    {
        var selected = string.IsNullOrWhiteSpace(preference) || preference == "follow"
            ? applicationLanguage : preference;
        return string.IsNullOrWhiteSpace(selected) || selected.StartsWith("cs", StringComparison.OrdinalIgnoreCase)
            ? Cs.Value : En.Value;
    }

    public string Get(string key) => _strings.TryGetValue(key, out var value)
        ? value : throw new InvalidOperationException($"Missing {Language} report key: {key}");

    public string Format(string key, params object?[] args) => string.Format(_culture, Get(key), args);
    public string Number(double value, string format) => value.ToString(format, _culture);
    public string Date(DateTime value) => value.ToString("g", _culture);

    // Only translate exact, known automatic messages. Never guess the meaning
    // of an old diagnostic or a technician's free text.
    public string? TranslateKnownRecommendation(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        foreach (string key in _strings.Keys.Where(k => k.StartsWith("CertificatePdf.Recommendation.", StringComparison.Ordinal)))
        {
            if ((Cs.Value._strings.TryGetValue(key, out var cs) && value == cs) ||
                (En.Value._strings.TryGetValue(key, out var en) && value == en))
                return _strings[key];
        }
        return value;
    }
}
