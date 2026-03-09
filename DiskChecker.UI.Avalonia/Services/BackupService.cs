using DiskChecker.UI.Avalonia.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Service for database backup and restore operations.
/// </summary>
public class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _databasePath;
    private readonly string _settingsPath;
    private readonly string _defaultBackupDir;

    public BackupService()
    {
        // Get application data directory
        var appDataDir = GetApplicationDataDirectory();
        _defaultBackupDir = Path.Combine(appDataDir, "Backups");
        
        // Database path - adjust based on actual location
        var projectDir = AppDomain.CurrentDomain.BaseDirectory;
        _databasePath = Path.Combine(projectDir, "DiskChecker.db");
        
        // Settings path
        _settingsPath = Path.Combine(appDataDir, "settings.json");
        
        // Ensure backup directory exists
        Directory.CreateDirectory(_defaultBackupDir);
    }

    private static string GetApplicationDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiskChecker");
        }
        else if (OperatingSystem.IsLinux())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DiskChecker");
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Try ApplicationSupport, fallback to .config
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configPath = Path.Combine(home, ".config");
            return Path.Combine(configPath, "DiskChecker");
        }
        
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiskChecker");
    }

    public async Task<string> CreateBackupAsync(string outputPath)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"DiskChecker_Backup_{timestamp}.zip";
        var fullPath = string.IsNullOrEmpty(outputPath) 
            ? Path.Combine(_defaultBackupDir, backupFileName)
            : outputPath;

        // Ensure directory exists
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Create backup metadata
        var metadata = new BackupMetadata
        {
            Version = "1.0.0",
            CreatedAt = DateTime.Now,
            DatabasePath = _databasePath,
            SettingsPath = _settingsPath
        };

        // Create ZIP file
        await Task.Run(() =>
        {
            using var zipArchive = ZipFile.Open(fullPath, ZipArchiveMode.Create);
            
            // Add database file if it exists
            if (File.Exists(_databasePath))
            {
                zipArchive.CreateEntryFromFile(_databasePath, "DiskChecker.db");
            }
            
            // Add WAL and SHM files if they exist
            var walPath = _databasePath + "-wal";
            var shmPath = _databasePath + "-shm";
            
            if (File.Exists(walPath))
            {
                zipArchive.CreateEntryFromFile(walPath, "DiskChecker.db-wal");
            }
            
            if (File.Exists(shmPath))
            {
                zipArchive.CreateEntryFromFile(shmPath, "DiskChecker.db-shm");
            }

            // Add settings file if it exists
            if (File.Exists(_settingsPath))
            {
                zipArchive.CreateEntryFromFile(_settingsPath, "settings.json");
            }

            // Add metadata
            var metadataJson = JsonSerializer.Serialize(metadata, _jsonOptions);
            var metadataEntry = zipArchive.CreateEntry("backup_metadata.json");
            using var writer = new StreamWriter(metadataEntry.Open());
            writer.Write(metadataJson);
        });

        return fullPath;
    }

    public async Task RestoreBackupAsync(string backupPath)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Backup file not found.", backupPath);
        }

        // Validate backup file
        using var zipArchive = ZipFile.OpenRead(backupPath);
        
        var dbEntry = zipArchive.GetEntry("DiskChecker.db");
        if (dbEntry == null)
        {
            throw new InvalidOperationException("Invalid backup file: database not found.");
        }

        await Task.Run(() =>
        {
            // Extract to temporary location first
            var tempDir = Path.Combine(Path.GetTempPath(), $"DiskChecker_Restore_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // Extract all files
                zipArchive.ExtractToDirectory(tempDir, overwriteFiles: true);
                
                // Get app data directory
                var appDataDir = GetApplicationDataDirectory();
                Directory.CreateDirectory(appDataDir);
                
                // Restore database
                var extractedDb = Path.Combine(tempDir, "DiskChecker.db");
                if (File.Exists(extractedDb))
                {
                    // Backup current database before restoring
                    if (File.Exists(_databasePath))
                    {
                        var preRestoreBackup = _databasePath + ".pre_restore";
                        File.Copy(_databasePath, preRestoreBackup, overwrite: true);
                    }
                    
                    File.Copy(extractedDb, _databasePath, overwrite: true);
                }

                // Restore WAL and SHM files
                var extractedWal = Path.Combine(tempDir, "DiskChecker.db-wal");
                var extractedShm = Path.Combine(tempDir, "DiskChecker.db-shm");
                
                if (File.Exists(extractedWal))
                {
                    File.Copy(extractedWal, _databasePath + "-wal", overwrite: true);
                }
                
                if (File.Exists(extractedShm))
                {
                    File.Copy(extractedShm, _databasePath + "-shm", overwrite: true);
                }

                // Restore settings
                var extractedSettings = Path.Combine(tempDir, "settings.json");
                if (File.Exists(extractedSettings))
                {
                    File.Copy(extractedSettings, _settingsPath, overwrite: true);
                }
            }
            finally
            {
                // Cleanup temp directory
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        });

        return true;
    }

    public string GetDefaultBackupDirectory()
    {
        return _defaultBackupDir;
    }

    public async Task<IEnumerable<IBackupService.BackupInfo>> GetAvailableBackupsAsync()
    {
        var backups = new List<BackupInfo>();
        
        await Task.Run(() =>
        {
            if (!Directory.Exists(_defaultBackupDir))
            {
                return;
            }

            var files = Directory.GetFiles(_defaultBackupDir, "*.zip");
            
            foreach (var file in files)
            {
                try
                {
                    using var zip = ZipFile.OpenRead(file);
                    var metadataEntry = zip.GetEntry("backup_metadata.json");
                    
                    var info = new IBackupService.BackupInfo
                    {
                        FileName = Path.GetFileName(file),
                        FilePath = file,
                        SizeBytes = new FileInfo(file).Length,
                        CreatedAt = File.GetCreationTime(file)
                    };

                    if (metadataEntry != null)
                    {
                        using var reader = new StreamReader(metadataEntry.Open());
                        var json = reader.ReadToEnd();
                        var metadata = JsonSerializer.Deserialize<BackupMetadata>(json);
                        if (metadata != null)
                        {
                            info.Version = metadata.Version;
                            info.CreatedAt = metadata.CreatedAt;
                        }
                    }

                    backups.Add(info);
                }
                catch
                {
                    // Skip invalid backup files
                }
            }
        });

        return backups.OrderByDescending(b => b.CreatedAt);
    }
    
    public async Task DeleteBackupAsync(string backupPath)
    {
        await Task.Run(() =>
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        });
    }
}