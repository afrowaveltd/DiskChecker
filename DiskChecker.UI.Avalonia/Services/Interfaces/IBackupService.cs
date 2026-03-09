namespace DiskChecker.UI.Avalonia.Services.Interfaces;

/// <summary>
/// Service for backup and restore operations.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Create a backup of the database.
    /// </summary>
    Task<string> CreateBackupAsync(string destinationPath);
    
    /// <summary>
    /// Restore database from a backup.
    /// </summary>
    Task RestoreBackupAsync(string backupPath);
    
    /// <summary>
    /// Get list of available backups with info.
    /// </summary>
    Task<IEnumerable<IBackupService.BackupInfo>> GetAvailableBackupsAsync();
    
    /// <summary>
    /// Delete a backup file.
    /// </summary>
    Task DeleteBackupAsync(string backupPath);
    
    /// <summary>
    /// Information about a backup.
    /// </summary>
    public class BackupInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }
        public string Version { get; set; } = "1.0.0";
    }
}