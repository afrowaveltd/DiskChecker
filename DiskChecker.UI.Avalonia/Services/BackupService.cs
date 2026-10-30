using DiskChecker.UI.Avalonia.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Metadata for a backup file.
/// </summary>
public class BackupMetadata
{
    public string Version { get; set; } = "1.0.0";
    public DateTime CreatedAt { get; set; }
    public string? DatabasePath { get; set; }
    public string? SettingsPath { get; set; }
}

/// <summary>
/// Service for database backup and restore operations.
/// </summary>
public class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;
    private readonly string _settingsPath;
    private readonly string _defaultBackupDir;

    public BackupService(
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory,
        DiskChecker.Application.Services.SettingsService settings)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        ArgumentNullException.ThrowIfNull(settings);
        _settingsPath = settings.SettingsFilePath;

        // Keep the legacy backup directory so existing backup ZIPs remain visible.
        _defaultBackupDir = Path.Combine(GetApplicationDataDirectory(), "Backups");
        Directory.CreateDirectory(_defaultBackupDir);
    }

    private static string GetApplicationDataDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskChecker");
        if (OperatingSystem.IsLinux())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DiskChecker");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "DiskChecker");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DiskChecker");
    }

    private string GetActiveSqliteConnectionString()
    {
        using var scope = _scopeFactory.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DiskChecker.Infrastructure.Persistence.DiskCheckerDbContext>();
        if (context.Database.GetDbConnection() is not Microsoft.Data.Sqlite.SqliteConnection connection)
            throw new NotSupportedException("Záloha ZIP podporuje pouze SQLite. Pro PostgreSQL/SQL Server použijte nástroje příslušné databáze.");
        return connection.ConnectionString;
    }

    public async Task<string> CreateBackupAsync(string destinationPath)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"DiskChecker_Backup_{timestamp}.zip";
        var fullPath = string.IsNullOrEmpty(destinationPath)
            ? Path.Combine(_defaultBackupDir, backupFileName)
            : destinationPath;
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        if (outputDir != null) Directory.CreateDirectory(outputDir);

        // BackupDatabase creates a consistent online SQLite snapshot including WAL
        // contents. Copying an arbitrary .db file from the executable directory did not.
        var cs = GetActiveSqliteConnectionString();
        var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs).DataSource;
        var tempDir = Path.Combine(Path.GetTempPath(), $"DiskChecker_BackupSnapshot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            await Task.Run(() =>
            {
                var snapshotPath = Path.Combine(tempDir, "DiskChecker.db");
                using (var source = new Microsoft.Data.Sqlite.SqliteConnection(cs))
                using (var target = new Microsoft.Data.Sqlite.SqliteConnection(
                    new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = snapshotPath }.ToString()))
                {
                    source.Open();
                    target.Open();
                    source.BackupDatabase(target);
                }

                var metadata = new BackupMetadata
                {
                    Version = "1.1.0",
                    CreatedAt = DateTime.Now,
                    DatabasePath = dataSource,
                    SettingsPath = _settingsPath
                };

                using var zip = ZipFile.Open(fullPath, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(snapshotPath, "DiskChecker.db");
                if (File.Exists(_settingsPath))
                    zip.CreateEntryFromFile(_settingsPath, "settings.json");
                var entry = zip.CreateEntry("backup_metadata.json");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(JsonSerializer.Serialize(metadata, _jsonOptions));
            });
            return fullPath;
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    public async Task RestoreBackupAsync(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup file not found.", backupPath);

        // Restore the *active* SQLite DB. Never overwrite a guessed application
        // directory DB. The application must be restarted after a restore.
        var cs = GetActiveSqliteConnectionString();
        var livePath = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs).DataSource;
        var tempDir = Path.Combine(Path.GetTempPath(), $"DiskChecker_Restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            using (var archive = ZipFile.OpenRead(backupPath))
            {
                var dbEntry = archive.GetEntry("DiskChecker.db")
                    ?? throw new InvalidDataException("Záloha neobsahuje DiskChecker.db.");
                archive.ExtractToDirectory(tempDir, overwriteFiles: true);
            }

            var importedPath = Path.Combine(tempDir, "DiskChecker.db");
            await Task.Run(() =>
            {
                using var source = new Microsoft.Data.Sqlite.SqliteConnection(
                    new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = importedPath }.ToString());
                source.Open();
                using (var verify = source.CreateCommand())
                {
                    verify.CommandText = "PRAGMA quick_check;";
                    if (!string.Equals(verify.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Obnovovaný soubor SQLite neprošel kontrolou integrity.");
                }

                // Preserve the currently active data before any replacement.
                var preRestore = livePath + ".pre_restore_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                using (var current = new Microsoft.Data.Sqlite.SqliteConnection(cs))
                using (var backup = new Microsoft.Data.Sqlite.SqliteConnection(
                    new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = preRestore }.ToString()))
                {
                    current.Open();
                    backup.Open();
                    current.BackupDatabase(backup);
                }

                using var destination = new Microsoft.Data.Sqlite.SqliteConnection(cs);
                destination.Open();
                source.BackupDatabase(destination);
            });

            // Old backup settings may point to a different DB: preserve the
            // active provider/location, while restoring unrelated preferences.
            var importedSettings = Path.Combine(tempDir, "settings.json");
            if (File.Exists(importedSettings))
            {
                var restored = System.Text.Json.Nodes.JsonNode.Parse(
                    await File.ReadAllTextAsync(importedSettings))?.AsObject();
                if (restored != null)
                {
                    if (File.Exists(_settingsPath))
                    {
                        var current = System.Text.Json.Nodes.JsonNode.Parse(
                            await File.ReadAllTextAsync(_settingsPath))?.AsObject();
                        if (current != null)
                        {
                            foreach (var key in new[] { "DatabaseProvider", "DatabaseConnectionString" })
                            {
                                if (current.TryGetPropertyValue(key, out var value))
                                    restored[key] = value?.DeepClone();
                                else
                                    restored.Remove(key);
                            }
                        }
                    }
                    await File.WriteAllTextAsync(_settingsPath, restored.ToJsonString(_jsonOptions));
                }
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    public string GetDefaultBackupDirectory()
    {
        return _defaultBackupDir;
    }

    public async Task<IEnumerable<IBackupService.BackupInfo>> GetAvailableBackupsAsync()
    {
        var backups = new List<IBackupService.BackupInfo>();
        
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