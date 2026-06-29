namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Provides localized strings for the application.
/// Implemented by the UI layer's LocaleService.
/// </summary>
public interface ILocaleProvider
{
    /// <summary>
    /// Gets a localized string by key.
    /// </summary>
    string GetString(string key);
    
    /// <summary>
    /// Gets a localized string by key with fallback.
    /// </summary>
    string GetString(string key, string fallback);
}
