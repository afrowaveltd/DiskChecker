using DiskChecker.Core.Interfaces;

namespace DiskChecker.Application.Services;

/// <summary>
/// Service for managing application settings.
/// </summary>
public class SettingsService : ISettingsService
{
    private bool _autoCheckForUpdates = true;
    private bool _runAtStartup;
    private bool _minimizeToTray = true;
    private int _autoSaveInterval = 5;
    private string _defaultExportPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private string _language = "cs";
    private bool _enableLogging = true;
    private string _logLevel = "Information";

    public Task<bool> GetAutoCheckForUpdatesAsync() => Task.FromResult(_autoCheckForUpdates);
    public Task SetAutoCheckForUpdatesAsync(bool value) { _autoCheckForUpdates = value; return Task.CompletedTask; }
    
    public Task<bool> GetRunAtStartupAsync() => Task.FromResult(_runAtStartup);
    public Task SetRunAtStartupAsync(bool value) { _runAtStartup = value; return Task.CompletedTask; }
    
    public Task<bool> GetMinimizeToTrayAsync() => Task.FromResult(_minimizeToTray);
    public Task SetMinimizeToTrayAsync(bool value) { _minimizeToTray = value; return Task.CompletedTask; }
    
    public Task<int> GetAutoSaveIntervalAsync() => Task.FromResult(_autoSaveInterval);
    public Task SetAutoSaveIntervalAsync(int value) { _autoSaveInterval = value; return Task.CompletedTask; }
    
    public Task<string> GetDefaultExportPathAsync() => Task.FromResult(_defaultExportPath);
    public Task SetDefaultExportPathAsync(string path) { _defaultExportPath = path; return Task.CompletedTask; }
    
    public Task<string> GetLanguageAsync() => Task.FromResult(_language);
    public Task SetLanguageAsync(string language) { _language = language; return Task.CompletedTask; }
    
    public Task<bool> GetEnableLoggingAsync() => Task.FromResult(_enableLogging);
    public Task SetEnableLoggingAsync(bool value) { _enableLogging = value; return Task.CompletedTask; }
    
    public Task<string> GetLogLevelAsync() => Task.FromResult(_logLevel);
    public Task SetLogLevelAsync(string level) { _logLevel = level; return Task.CompletedTask; }
    
    public Task ResetToDefaultsAsync()
    {
        _autoCheckForUpdates = true;
        _runAtStartup = false;
        _minimizeToTray = true;
        _autoSaveInterval = 5;
        _defaultExportPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _language = "cs";
        _enableLogging = true;
        _logLevel = "Information";
        return Task.CompletedTask;
    }
}