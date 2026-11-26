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

        // Keep backups under the same user-config directory as settings.
        // The old hard-coded LocalAppData backup folder was never used in production.
        _defaultBackupDir = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "Backups");
        Directory.CreateDirectory(_defaultBackupDir);
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
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var backupFileName = $"DiskChecker_Backup_{timestamp}_{Guid.NewGuid():N}.zip";
        var fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(destinationPath)
            ? Path.Combine(_defaultBackupDir, backupFileName)
            : Directory.Exists(destinationPath)
                ? Path.Combine(destinationPath, backupFileName)
                : destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath))
            throw new IOException("The backup destination already exists.");
        // Publish only a completed ZIP; keep a failed write out of backup listings.
        var temporaryArchive = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

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
                    new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = snapshotPath, Pooling = false }.ToString()))
                {
                    source.Open();
                    target.Open();
                    source.BackupDatabase(target);
                    MakeStandaloneSnapshot(target);
                }

                var metadata = new BackupMetadata
                {
                    Version = "1.1.0",
                    CreatedAt = DateTime.Now,
                    DatabasePath = dataSource,
                    SettingsPath = _settingsPath
                };

                using var zip = ZipFile.Open(temporaryArchive, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(snapshotPath, "DiskChecker.db");
                if (File.Exists(_settingsPath))
                    zip.CreateEntryFromFile(_settingsPath, "settings.json");
                var entry = zip.CreateEntry("backup_metadata.json");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(JsonSerializer.Serialize(metadata, _jsonOptions));
            });
            File.Move(temporaryArchive, fullPath);
            return fullPath;
        }
        finally
        {
            if (File.Exists(temporaryArchive))
                File.Delete(temporaryArchive);
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
        var preRestore = livePath + ".pre_restore_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N");
        string? temporarySettings = null;
        bool databaseReplacementStarted = false;
        Directory.CreateDirectory(tempDir);
        try
        {
            using (var archive = ZipFile.OpenRead(backupPath))
            {
                var dbEntry = archive.GetEntry("DiskChecker.db")
                    ?? throw new InvalidDataException("Záloha neobsahuje DiskChecker.db.");
                // Do not extract arbitrary archive entries. Only these two files are supported.
                dbEntry.ExtractToFile(Path.Combine(tempDir, "DiskChecker.db"));
                archive.GetEntry("settings.json")?.ExtractToFile(Path.Combine(tempDir, "settings.json"));
            }

            // Validate settings before touching the live database. A malformed ZIP
            // must not report failure only after replacing the current database.
            var importedSettings = Path.Combine(tempDir, "settings.json");
            string? restoredSettings = null;
            if (File.Exists(importedSettings))
            {
                var restored = System.Text.Json.Nodes.JsonNode.Parse(
                    await File.ReadAllTextAsync(importedSettings)) as System.Text.Json.Nodes.JsonObject
                    ?? throw new InvalidDataException("Backup settings must be a JSON object.");
                if (File.Exists(_settingsPath))
                {
                    var current = System.Text.Json.Nodes.JsonNode.Parse(
                        await File.ReadAllTextAsync(_settingsPath)) as System.Text.Json.Nodes.JsonObject
                        ?? throw new InvalidDataException("Current settings must be a JSON object.");
                    foreach (var key in new[] { "DatabaseProvider", "DatabaseConnectionString" })
                    {
                        if (current.TryGetPropertyValue(key, out var value))
                            restored[key] = value?.DeepClone();
                        else
                            restored.Remove(key);
                    }
                }
                else
                {
                    // Never adopt a database location from another machine.
                    restored.Remove("DatabaseProvider");
                    restored.Remove("DatabaseConnectionString");
                }
                restoredSettings = restored.ToJsonString(_jsonOptions);
            }

            // Stage the settings beside their destination before changing the database.
            // A full or unwritable settings volume therefore fails before replacement.
            if (restoredSettings != null)
            {
                temporarySettings = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllTextAsync(temporarySettings, restoredSettings);
            }

            var importedPath = Path.Combine(tempDir, "DiskChecker.db");
            await Task.Run(() =>
            {
                using var source = new Microsoft.Data.Sqlite.SqliteConnection(
                    new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = importedPath, Pooling = false }.ToString());
                source.Open();
                using (var verify = source.CreateCommand())
                {
                    verify.CommandText = "PRAGMA quick_check;";
                    if (!string.Equals(verify.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Obnovovaný soubor SQLite neprošel kontrolou integrity.");
                }

                ValidateDiagnosticSchema(source);

                // Preserve the currently active data before any replacement.
                using (var current = new Microsoft.Data.Sqlite.SqliteConnection(cs))
                using (var backup = new Microsoft.Data.Sqlite.SqliteConnection(
                    new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = preRestore, Pooling = false }.ToString()))
                {
                    current.Open();
                    backup.Open();
                    current.BackupDatabase(backup);
                    MakeStandaloneSnapshot(backup);
                }

                using var destination = new Microsoft.Data.Sqlite.SqliteConnection(cs);
                destination.Open();
                databaseReplacementStarted = true;
                source.BackupDatabase(destination);
            });

            if (temporarySettings != null)
                PublishRestoredSettings(temporarySettings, _settingsPath);
        }
        catch (Exception restoreError) when (databaseReplacementStarted)
        {
            // SQLite and the settings file are separate resources. If publishing
            // settings or replacing the database fails, compensate from the
            // pre-restore snapshot. Keep that snapshot for manual recovery.
            try
            {
                await Task.Run(() =>
                {
                    using var original = new Microsoft.Data.Sqlite.SqliteConnection(
                        new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                        {
                            DataSource = preRestore,
                            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                            Pooling = false
                        }.ToString());
                    using var destination = new Microsoft.Data.Sqlite.SqliteConnection(cs);
                    original.Open();
                    destination.Open();
                    original.BackupDatabase(destination);
                });
            }
            catch (Exception rollbackError)
            {
                throw new IOException(
                    $"Restore and automatic database rollback failed. Recovery snapshot: {preRestore}",
                    new AggregateException(restoreError, rollbackError));
            }

            throw new IOException(
                $"Restore failed; the original database was recovered. Recovery snapshot: {preRestore}",
                restoreError);
        }
        finally
        {
            if (temporarySettings != null && File.Exists(temporarySettings))
                File.Delete(temporarySettings);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // quick_check validates SQLite's storage, not the application's schema.
    // Verify the core historical tables and their identity columns before
    // touching live data. Optional columns/tables remain migration-compatible.
    // A recovery snapshot is a standalone file, not a live WAL database.
    // BackupDatabase can copy the source's journal mode. Normalize it before
    // closing, archiving or reopening the snapshot in read-only mode.
    private static void MakeStandaloneSnapshot(Microsoft.Data.Sqlite.SqliteConnection snapshot)
    {
        using var command = snapshot.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE;";
        if (!string.Equals(command.ExecuteScalar()?.ToString(), "delete", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Could not finalize a standalone SQLite recovery snapshot.");
    }

    private static void ValidateDiagnosticSchema(Microsoft.Data.Sqlite.SqliteConnection source)
    {
        string[] requiredQueries =
        {
            "SELECT Id, SerialNumber, ModelName FROM DiskCards LIMIT 0;",
            "SELECT Id, DiskCardId, SessionId, StartedAt FROM TestSessions LIMIT 0;",
            "SELECT Id, DiskCardId, TestSessionId, CertificateNumber FROM DiskCertificates LIMIT 0;"
        };
        try
        {
            foreach (var query in requiredQueries)
            {
                using var command = source.CreateCommand();
                command.CommandText = query;
                using var reader = command.ExecuteReader();
            }

            // A certificate must refer to a test of the same disk, not merely
            // to a numerically valid foreign key in another disk's history.
            using var relationships = source.CreateCommand();
            relationships.CommandText = """
                SELECT 1 FROM DiskCertificates AS c
                LEFT JOIN TestSessions AS s ON s.Id = c.TestSessionId
                LEFT JOIN DiskCards AS d ON d.Id = c.DiskCardId
                WHERE s.Id IS NULL OR d.Id IS NULL OR s.DiskCardId <> c.DiskCardId
                LIMIT 1;
                """;
            if (relationships.ExecuteScalar() != null)
                throw new InvalidDataException("Backup contains a certificate linked to a missing or unrelated test.");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            throw new InvalidDataException("Backup does not contain a compatible DiskChecker diagnostic database.", ex);
        }
    }

    /// <summary>Publishes staged settings with a same-directory replacement.</summary>
    protected virtual void PublishRestoredSettings(string temporaryPath, string destinationPath)
    {
        File.Move(temporaryPath, destinationPath, overwrite: true);
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
                    if (zip.GetEntry("DiskChecker.db") is not { Length: > 0 })
                        continue;
                    var metadataEntry = zip.GetEntry("backup_metadata.json");
                    if (metadataEntry is { Length: > 1048576 })
                        continue;
                    
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