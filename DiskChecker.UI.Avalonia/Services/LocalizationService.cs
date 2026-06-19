using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Flat JSON dictionary localization service.
/// Loads locale files from the Locales folder (e.g. cs.json, en.json).
/// Falls back to Czech if a key is missing in the current locale.
/// </summary>
public class LocaleService
{
    private const string LocalesFolder = "Locales";
    private const string FallbackLocale = "cs";

    private readonly Dictionary<string, string> _current = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fallback = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLocale = FallbackLocale;

    public string CurrentLocale => _currentLocale;

    public event Action? LocaleChanged;

    public LocaleService()
    {
        LoadLocale(FallbackLocale, _fallback);
        _currentLocale = FallbackLocale;
        // Copy fallback to current initially
        foreach (var kv in _fallback)
            _current[kv.Key] = kv.Value;
    }

    /// <summary>
    /// Get a localized string by key. Falls back to Czech if missing.
    /// </summary>
    public string Get(string key)
    {
        if (_current.TryGetValue(key, out var value))
            return value;
        if (_fallback.TryGetValue(key, out var fb))
            return fb;
        return $"[[{key}]]";
    }

    /// <summary>
    /// Get a localized string with formatting arguments.
    /// </summary>
    public string Get(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }

    /// <summary>
    /// Switch to a different locale. Loads the JSON file and merges with fallback.
    /// </summary>
    public void SetLocale(string locale)
    {
        _current.Clear();
        LoadLocale(locale, _current);
        _currentLocale = locale;
        LocaleChanged?.Invoke();
    }

    /// <summary>
    /// Get all available locales by scanning the Locales folder.
    /// </summary>
    public IReadOnlyList<string> GetAvailableLocales()
    {
        var list = new List<string>();
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var localesDir = Path.Combine(baseDir, LocalesFolder);
            if (Directory.Exists(localesDir))
            {
                foreach (var file in Directory.GetFiles(localesDir, "*.json"))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    list.Add(name);
                }
            }
        }
        catch { /* ignore scan errors */ }
        return list;
    }

    private static void LoadLocale(string locale, Dictionary<string, string> target)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(baseDir, LocalesFolder, $"{locale}.json");
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
            {
                foreach (var kv in dict)
                    target[kv.Key] = kv.Value;
            }
        }
        catch { /* ignore load errors, fallback will handle */ }
    }
}
