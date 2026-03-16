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
    
    /// <summary>
    /// Gets the e-mail address used as recipient for automatic test reports.
    /// </summary>
    Task<string> GetReportRecipientEmailAsync();

    /// <summary>
    /// Sets the e-mail address used as recipient for automatic test reports.
    /// </summary>
    Task SetReportRecipientEmailAsync(string email);
    
    // Theme settings
    /// <summary>
    /// Gets whether dark theme is enabled.
    /// </summary>
    Task<bool> GetIsDarkThemeAsync();
    
    /// <summary>
    /// Sets the dark theme preference.
    /// </summary>
    Task SetIsDarkThemeAsync(bool isDark);

    // SMART probe configuration (persisted)
    Task<int> GetSmartCacheTtlMinutesAsync();
    Task SetSmartCacheTtlMinutesAsync(int minutes);
    Task<int> GetSmartProbeTimeoutSecondsAsync();
    Task SetSmartProbeTimeoutSecondsAsync(int seconds);
    Task<int> GetSmartProbeParallelismAsync();
    Task SetSmartProbeParallelismAsync(int parallelism);
    // Persisted setter for SMART cache TTL
    Task SetSmartCacheTtlMinutesAsyncPersistent(int minutes);
    
    // Disk Lock Management
    /// <summary>
    /// Gets the list of locked disk paths (serial numbers or device paths).
    /// </summary>
    Task<List<string>> GetLockedDisksAsync();
    
    /// <summary>
    /// Sets the list of locked disk paths.
    /// </summary>
    Task SetLockedDisksAsync(List<string> lockedPaths);
    
    /// <summary>
    /// Checks if a disk is locked.
    /// </summary>
    Task<bool> IsDiskLockedAsync(string diskPath);
    
    /// <summary>
    /// Locks a disk (adds to locked list).
    /// </summary>
    Task LockDiskAsync(string diskPath);
    
    /// <summary>
    /// Unlocks a disk (removes from locked list).
    /// </summary>
    Task UnlockDiskAsync(string diskPath);
}