using Guid = System.Guid;

namespace DiskChecker.UI.Avalonia.Services.Interfaces;

// NOTE: Individual service interfaces have been moved to separate files:
// - INavigationService.cs
// - IDiskSelectionService.cs
// - IDialogService.cs
// - IAnalysisService.cs
// - IHistoryService.cs
// - IBackupService.cs
// 
// This file is kept for backward compatibility only.

// Re-export all service interfaces for backward compatibility
// New code should use the individual interface files

public type INavigationService = INavigationService;
public type IDiskSelectionService = IDiskSelectionService;
public type IDialogService = IDialogService;
public type IAnalysisService = IAnalysisService;
public type IHistoryService = IHistoryService;
public type IBackupService = IBackupService;

public interface IDialogService
{
    Task ShowMessageAsync(string title, string message);
    Task ShowErrorAsync(string title, string message);
    Task<bool> ShowConfirmationAsync(string title, string message);
}

public interface IAnalysisService
{
    // AnalyzeSurfaceAsync - pro volání z AnalysisViewModel (přijímá string deviceId a Progress)
    Task<IEnumerable<SurfaceTestResult>> AnalyzeSurfaceAsync(string deviceId, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    Task CancelAnalysisAsync();
}

public interface IHistoryService
{
    // GetHistoricalTestsAsync - vrací IEnumerable<HistoricalTest>
    Task<IEnumerable<HistoricalTest>> GetHistoricalTestsAsync();
    
    // GetTestByIdAsync - přijímá int (HistoricalTest.TestId je int)
    Task<HistoricalTest?> GetTestByIdAsync(int testId);
    
    // SaveTestAsync - přijímá HistoricalTest
    Task SaveTestAsync(HistoricalTest test);
    
    // DeleteHistoricalTestAsync - přijímá int (HistoricalTest.TestId je int)
    Task DeleteHistoricalTestAsync(int testId);
}

public interface IReportService
{
    // GetReportsAsync - vrací IEnumerable<TestReport>
    Task<IEnumerable<TestReport>> GetReportsAsync();
    
    // GetReportByIdAsync - přijímá Guid
    Task<TestReport?> GetReportByIdAsync(Guid reportId);
    
    // GenerateReportAsync - BEZ argumentů (vrací nový report)
    Task<TestReport> GenerateReportAsync();
    
    // DeleteReportAsync - přijímá Guid
    Task DeleteReportAsync(Guid reportId);
    
    // ExportReportAsync - přijímá TestReport a string format (bez reportId)
    Task ExportReportAsync(TestReport report, string format);
}

public interface ISettingsService
{
    // Application settings - vrací správné typy
    Task<bool> GetAutoCheckForUpdatesAsync();
    Task SetAutoCheckForUpdatesAsync(bool value);
    
    Task<bool> GetRunAtStartupAsync();
    Task SetRunAtStartupAsync(bool value);
    
    Task<bool> GetMinimizeToTrayAsync();
    Task SetMinimizeToTrayAsync(bool value);
    
    Task<int> GetAutoSaveIntervalAsync();
    Task SetAutoSaveIntervalAsync(int minutes);
    
    Task<string> GetDefaultExportPathAsync();
    Task SetDefaultExportPathAsync(string path);
    
    Task<string> GetLanguageAsync();
    Task SetLanguageAsync(string language);
    
    Task<bool> GetEnableLoggingAsync();
    Task SetEnableLoggingAsync(bool value);
    
    // LogLevel je string (opraveno z int)
    Task<string> GetLogLevelAsync();
    Task SetLogLevelAsync(string level);
    
    // Email settings
    Task<EmailSettings?> GetEmailSettingsAsync();
    Task SaveEmailSettingsAsync(EmailSettings settings);
    
    Task ResetToDefaultsAsync();
}

/// <summary>
/// Service for database backup and restore operations.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a backup of the database and settings to a ZIP file.
    /// </summary>
    /// <param name="outputPath">Path where the ZIP file will be saved.</param>
    /// <returns>Path to the created backup file.</returns>
    Task<string> CreateBackupAsync(string outputPath);
    
    /// <summary>
    /// Restores database and settings from a backup ZIP file.
    /// </summary>
    /// <param name="backupPath">Path to the backup ZIP file.</param>
    /// <returns>True if restore was successful.</returns>
    Task<bool> RestoreBackupAsync(string backupPath);
    
    /// <summary>
    /// Gets the default backup directory.
    /// </summary>
    string GetDefaultBackupDirectory();
    
    /// <summary>
    /// Gets list of available backups in the backup directory.
    /// </summary>
    Task<IEnumerable<BackupInfo>> GetAvailableBackupsAsync();
}

/// <summary>
/// Information about a backup file.
/// </summary>
public class BackupInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
    public string Version { get; set; } = string.Empty;
}
