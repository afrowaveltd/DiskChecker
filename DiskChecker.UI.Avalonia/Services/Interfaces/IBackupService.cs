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
    /// Get list of available backups.
    /// </summary>
    Task<IEnumerable<string>> GetAvailableBackupsAsync();
    
    /// <summary>
    /// Delete a backup file.
    /// </summary>
    Task DeleteBackupAsync(string backupPath);
    
    /// <summary>
    /// Information about a backup.
    /// </summary>
    public class BackupInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }
    }
}
