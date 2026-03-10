namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Service for managing application settings.
/// </summary>
public interface ISettingsService
{
    Task<bool> GetAutoCheckForUpdatesAsync();
    Task SetAutoCheckForUpdatesAsync(bool value);
    Task<bool> GetRunAtStartupAsync();
    Task SetRunAtStartupAsync(bool value);
    Task<bool> GetMinimizeToTrayAsync();
    Task SetMinimizeToTrayAsync(bool value);
    Task<int> GetAutoSaveIntervalAsync();
    Task SetAutoSaveIntervalAsync(int value);
    Task<string> GetDefaultExportPathAsync();
    Task SetDefaultExportPathAsync(string path);
    Task<string> GetLanguageAsync();
    Task SetLanguageAsync(string language);
    Task<bool> GetEnableLoggingAsync();
    Task SetEnableLoggingAsync(bool value);
    Task<string> GetLogLevelAsync();
    Task SetLogLevelAsync(string level);
    Task ResetToDefaultsAsync();
}