using DiskChecker.Core.Interfaces;
using System.Text.Json;
using System.Linq;

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
    private List<string> _lockedDisks = new();
    private bool _isDarkTheme;
    private string _reportRecipientEmail = string.Empty;
    // SMART probe persisted settings
    private int _smartCacheTtlMinutes = 10;
    private int _smartProbeTimeoutSeconds = 4;
    private int _smartProbeParallelism; // 0 = auto
    private int _usbRecoveryRetryCount = 2;
    private bool _emailSendOnlyForLongRunningTests = true;
    private bool _emailIncludeCertificateAttachment = true;
    
    private readonly string _settingsFilePath;
    private readonly string _lockedDisksFilePath;
    private readonly string _darkThemeFilePath;
    
    // Cached JsonSerializerOptions for performance
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    
    public SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "DiskChecker");
        Directory.CreateDirectory(appFolder);
        
        _settingsFilePath = Path.Combine(appFolder, "settings.json");
        _lockedDisksFilePath = Path.Combine(appFolder, "locked_disks.json");
        _darkThemeFilePath = Path.Combine(appFolder, "dark_theme");

        LoadSettingsFromFile();
        LoadLockedDisksFromFile();
        LoadDarkThemeFromFile();
    }

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
    
    public Task<string> GetReportRecipientEmailAsync() => Task.FromResult(_reportRecipientEmail);
    public Task<bool> GetEmailIncludeCertificateAttachmentAsync() => Task.FromResult(_emailIncludeCertificateAttachment);
    public Task<bool> GetEmailSendOnlyForLongRunningTestsAsync() => Task.FromResult(_emailSendOnlyForLongRunningTests);
    public Task<int> GetUsbRecoveryRetryCountAsync() => Task.FromResult(_usbRecoveryRetryCount);

    public Task SetUsbRecoveryRetryCountAsync(int value)
    {
        _usbRecoveryRetryCount = Math.Clamp(value, 0, 10);
        SaveSettingsToFile();
        return Task.CompletedTask;
    }
 
     public Task SetEmailSendOnlyForLongRunningTestsAsync(bool value)
    {
        _emailSendOnlyForLongRunningTests = value;
        SaveSettingsToFile();
        return Task.CompletedTask;
    }

    public Task SetEmailIncludeCertificateAttachmentAsync(bool value)
    {
        _emailIncludeCertificateAttachment = value;
        SaveSettingsToFile();
        return Task.CompletedTask;
    }
 
     public Task SetReportRecipientEmailAsync(string email)
    {
        _reportRecipientEmail = email?.Trim() ?? string.Empty;
        SaveSettingsToFile();
        return Task.CompletedTask;
    }
    
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
        _lockedDisks = new List<string>();
        _smartCacheTtlMinutes = 10;
        _smartProbeTimeoutSeconds = 4;
        _smartProbeParallelism = 0;
        _usbRecoveryRetryCount = 2;
        _reportRecipientEmail = string.Empty;
        _emailSendOnlyForLongRunningTests = true;
        _emailIncludeCertificateAttachment = true;
        SaveLockedDisksToFile();
        SaveSettingsToFile();
        return Task.CompletedTask;
    }
    
    // Theme settings
    public Task<bool> GetIsDarkThemeAsync() => Task.FromResult(_isDarkTheme);
    
    public Task SetIsDarkThemeAsync(bool isDark)
    {
        _isDarkTheme = isDark;
        SaveDarkThemeToFile();
        return Task.CompletedTask;
    }
    
    private void LoadDarkThemeFromFile()
    {
        try
        {
            _isDarkTheme = File.Exists(_darkThemeFilePath);
        }
        catch
        {
            _isDarkTheme = false;
        }
    }
    
    private void SaveDarkThemeToFile()
    {
        try
        {
            if (_isDarkTheme)
            {
                // Create empty file to indicate dark theme
                File.WriteAllText(_darkThemeFilePath, string.Empty);
            }
            else
            {
                // Delete file to indicate light theme
                if (File.Exists(_darkThemeFilePath))
                {
                    File.Delete(_darkThemeFilePath);
                }
            }
        }
        catch
        {
            // Ignore file errors
        }
    }
    
    // Disk Lock Management
    public Task<List<string>> GetLockedDisksAsync() => Task.FromResult(_lockedDisks);
    
    public Task SetLockedDisksAsync(List<string> lockedPaths)
    {
        _lockedDisks = lockedPaths ?? new List<string>();
        SaveLockedDisksToFile();
        return Task.CompletedTask;
    }

    // SMART settings persisted
    public Task<int> GetSmartCacheTtlMinutesAsync() => Task.FromResult(_smartCacheTtlMinutes);
    public Task SetSmartCacheTtlMinutesAsync(int minutes) { _smartCacheTtlMinutes = Math.Max(1, minutes); return Task.CompletedTask; }
    public Task<int> GetSmartProbeTimeoutSecondsAsync() => Task.FromResult(_smartProbeTimeoutSeconds);
    public Task SetSmartProbeTimeoutSecondsAsync(int seconds) { _smartProbeTimeoutSeconds = Math.Max(1, seconds); SaveSettingsToFile(); return Task.CompletedTask; }
    public Task<int> GetSmartProbeParallelismAsync() => Task.FromResult(_smartProbeParallelism);
    public Task SetSmartProbeParallelismAsync(int parallelism) { _smartProbeParallelism = Math.Max(0, parallelism); SaveSettingsToFile(); return Task.CompletedTask; }
    public Task SetSmartCacheTtlMinutesAsyncPersistent(int minutes) { _smartCacheTtlMinutes = Math.Max(1, minutes); SaveSettingsToFile(); return Task.CompletedTask; }

    private void LoadSettingsFromFile()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var doc = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (doc != null)
                {
                    if (doc.TryGetValue("SmartCacheTtlMinutes", out var ttlObj) && int.TryParse(ttlObj.ToString(), out var ttl))
                        _smartCacheTtlMinutes = Math.Max(1, ttl);
                    if (doc.TryGetValue("SmartProbeTimeoutSeconds", out var toObj) && int.TryParse(toObj.ToString(), out var to))
                        _smartProbeTimeoutSeconds = Math.Max(1, to);
                    if (doc.TryGetValue("SmartProbeParallelism", out var pObj) && int.TryParse(pObj.ToString(), out var p))
                        _smartProbeParallelism = Math.Max(0, p);
                    if (doc.TryGetValue("UsbRecoveryRetryCount", out var usbRetryObj) && int.TryParse(usbRetryObj?.ToString(), out var usbRetryCount))
                        _usbRecoveryRetryCount = Math.Clamp(usbRetryCount, 0, 10);
                    if (doc.TryGetValue("ReportRecipientEmail", out var recipientObj))
                        _reportRecipientEmail = recipientObj?.ToString()?.Trim() ?? string.Empty;
                    if (doc.TryGetValue("EmailSendOnlyForLongRunningTests", out var sendLongOnlyObj) && bool.TryParse(sendLongOnlyObj?.ToString(), out var sendLongOnly))
                        _emailSendOnlyForLongRunningTests = sendLongOnly;
                    if (doc.TryGetValue("EmailIncludeCertificateAttachment", out var includeAttachmentObj) && bool.TryParse(includeAttachmentObj?.ToString(), out var includeAttachment))
                        _emailIncludeCertificateAttachment = includeAttachment;
                }
            }
        }
        catch
        {
            // ignore and use defaults
        }
    }

    private void SaveSettingsToFile()
    {
        try
        {
            var dict = new Dictionary<string, object>
            {
                ["SmartCacheTtlMinutes"] = _smartCacheTtlMinutes,
                ["SmartProbeTimeoutSeconds"] = _smartProbeTimeoutSeconds,
                ["SmartProbeParallelism"] = _smartProbeParallelism,
                ["UsbRecoveryRetryCount"] = _usbRecoveryRetryCount,
                ["ReportRecipientEmail"] = _reportRecipientEmail,
                ["EmailSendOnlyForLongRunningTests"] = _emailSendOnlyForLongRunningTests,
                ["EmailIncludeCertificateAttachment"] = _emailIncludeCertificateAttachment
            };
            var json = JsonSerializer.Serialize(dict, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // ignore save errors
        }
    }
    
    public Task<bool> IsDiskLockedAsync(string diskPath)
    {
        if (string.IsNullOrEmpty(diskPath)) return Task.FromResult(false);
        return Task.FromResult(_lockedDisks.Any(p => IsSameDiskk(p, diskPath)));
    }
    
    public Task LockDiskAsync(string diskPath)
    {
        if (string.IsNullOrEmpty(diskPath)) return Task.CompletedTask;
        
        // Check if already locked (by path or serial)
        if (!_lockedDisks.Any(p => IsSameDiskk(p, diskPath)))
        {
            _lockedDisks.Add(diskPath);
            SaveLockedDisksToFile();
        }
        return Task.CompletedTask;
    }
    
    public Task UnlockDiskAsync(string diskPath)
    {
        if (string.IsNullOrEmpty(diskPath)) return Task.CompletedTask;
        
        _lockedDisks.RemoveAll(p => IsSameDiskk(p, diskPath));
        SaveLockedDisksToFile();
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Checks if two disk identifiers refer to the same disk.
    /// Supports both paths (\\.\PhysicalDrive0) and serial numbers.
    /// </summary>
    private static bool IsSameDiskk(string identifier1, string identifier2)
    {
        if (string.IsNullOrEmpty(identifier1) || string.IsNullOrEmpty(identifier2)) return false;
        
        // Direct match
        if (string.Equals(identifier1, identifier2, StringComparison.OrdinalIgnoreCase)) return true;
        
        // Extract drive number from path
        var num1 = ExtractDriveNumber(identifier1);
        var num2 = ExtractDriveNumber(identifier2);
        
        // If both have drive numbers, compare them
        if (num1.HasValue && num2.HasValue)
        {
            return num1.Value == num2.Value;
        }
        
        // Check if serial numbers match
        if (identifier1.StartsWith("SERIAL:", StringComparison.OrdinalIgnoreCase) && 
            identifier2.StartsWith("SERIAL:", StringComparison.OrdinalIgnoreCase))
        {
            var serial1 = identifier1.Substring(7).ToUpperInvariant();
            var serial2 = identifier2.Substring(7).ToUpperInvariant();
            return serial1 == serial2;
        }
        
        return false;
    }
    
    /// <summary>
    /// Extracts the drive number from a device path like "\\.\PhysicalDrive0"
    /// </summary>
    private static int? ExtractDriveNumber(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        
        // Match patterns like PhysicalDrive0, pd0, /dev/pd0
        var digits = new string(path.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var num))
        {
            return num;
        }
        return null;
    }
    
    private void LoadLockedDisksFromFile()
    {
        try
        {
            if (File.Exists(_lockedDisksFilePath))
            {
                var json = File.ReadAllText(_lockedDisksFilePath);
                var disks = JsonSerializer.Deserialize<List<string>>(json);
                if (disks != null)
                {
                    _lockedDisks = disks;
                }
            }
        }
        catch
        {
            _lockedDisks = new List<string>();
        }
        
        // Always lock system disk (PhysicalDrive0) by default
        var systemDiskPath = "\\\\.\\PhysicalDrive0";
        if (!_lockedDisks.Contains(systemDiskPath))
        {
            _lockedDisks.Insert(0, systemDiskPath);
        }
    }
    
    private void SaveLockedDisksToFile()
    {
        try
        {
            var json = JsonSerializer.Serialize(_lockedDisks, JsonOptions);
            File.WriteAllText(_lockedDisksFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }
}