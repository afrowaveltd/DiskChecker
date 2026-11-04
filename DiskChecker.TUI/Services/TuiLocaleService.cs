using System.Text.Json;
using DiskChecker.Core.Interfaces;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Flat JSON locale provider shared with the desktop application. It reads the
/// exact same \`Locales/*.json\` files so both front ends use one vocabulary.
/// Missing keys fall back to Czech and finally to a visible [[key]] marker.
/// </summary>
public sealed class TuiLocaleService : ILocaleProvider
{
    private const string FallbackLocale = "cs";
    private readonly Dictionary<string, string> _current = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fallback = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _localesDirectory;
    private string _currentLocale = FallbackLocale;

    public TuiLocaleService(string? localesDirectory = null)
    {
        _localesDirectory = localesDirectory ?? Path.Combine(AppContext.BaseDirectory, "Locales");
        LoadLocale(FallbackLocale, _fallback);
        foreach (var pair in _fallback)
        {
            _current[pair.Key] = pair.Value;
        }
    }

    public event EventHandler? LocaleChanged;

    public string CurrentLocale => _currentLocale;

    public int KeyCount => _current.Count;

    public IReadOnlyList<string> AvailableLocales
    {
        get
        {
            try
            {
                if (!Directory.Exists(_localesDirectory))
                {
                    return new[] { FallbackLocale };
                }

                return Directory.GetFiles(_localesDirectory, "*.json")
                    .Select(path => Path.GetFileNameWithoutExtension(path))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (IOException)
            {
                return new[] { FallbackLocale };
            }
        }
    }

    public void SetLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return;
        }

        _current.Clear();
        LoadLocale(locale, _current);
        if (_current.Count == 0)
        {
            LoadLocale(FallbackLocale, _current);
            locale = FallbackLocale;
        }

        _currentLocale = locale;
        LocaleChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key) => GetString(key);

    public string Get(string key, params object[] args)
    {
        string template = GetString(key);
        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_current.TryGetValue(key, out string? value))
        {
            return value;
        }

        return _fallback.TryGetValue(key, out string? fallback) ? fallback : $"[[{key}]]";
    }

    public string GetString(string key, string fallback) =>
        _current.TryGetValue(key, out string? value)
            ? value
            : _fallback.TryGetValue(key, out string? fallbackValue)
                ? fallbackValue
                : fallback;

    private void LoadLocale(string locale, Dictionary<string, string> target)
    {
        try
        {
            string path = Path.Combine(_localesDirectory, locale + ".json");
            if (!File.Exists(path))
            {
                return;
            }

            var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (dictionary is null)
            {
                return;
            }

            foreach (var pair in dictionary)
            {
                target[pair.Key] = pair.Value;
            }
        }
        catch (IOException)
        {
            // Missing or unreadable locale files degrade to the Czech fallback.
        }
        catch (JsonException)
        {
            // Malformed locale files degrade to the Czech fallback.
        }
    }
}
