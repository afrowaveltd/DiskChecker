using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DiskChecker.TUI.Services;

/// <summary>Metadata stored inside an application data ZIP backup.</summary>
public sealed class TuiDataBackupMetadata
{
    public string Version { get; set; } = "1.1.0";

    public DateTime CreatedAt { get; set; }

    public string? DatabasePath { get; set; }

    public string? SettingsPath { get; set; }
}

/// <summary>One application data ZIP backup available for restore.</summary>
public sealed record TuiDataBackupInfo(
    string FilePath,
    string FileName,
    long SizeBytes,
    DateTime CreatedAt,
    string Version);

/// <summary>Credential-free description of the active database.</summary>
public sealed record TuiDatabaseInfo(
    string Provider,
    string Location,
    long SizeBytes,
    bool IsSqlite,
    string Description);

/// <summary>Outcome of restoring an application data ZIP backup.</summary>
public sealed record TuiDataRestoreOutcome(
    string PreRestorePath,
    bool SettingsRestored,
    string Message);

/// <summary>
/// Terminal counterpart of the desktop <c>BackupService</c>: creates and restores a ZIP
/// archive holding the active SQLite database, <c>settings.json</c> and
/// <c>backup_metadata.json</c>. The live database is never replaced without a
/// <c>.pre_restore_*</c> copy and a <c>PRAGMA quick_check</c> of the imported file.
/// </summary>
public sealed class TuiDataBackupService
{
    private const string DatabaseEntryName = "DiskChecker.db";
    private const string SettingsEntryName = "settings.json";
    private const string MetadataEntryName = "backup_metadata.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IServiceScopeFactory _scopeFactory;

    public TuiDataBackupService(IServiceScopeFactory scopeFactory, string settingsFilePath, string? backupDirectory = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);

        SettingsPath = settingsFilePath;
        string? settingsDirectory = Path.GetDirectoryName(Path.GetFullPath(settingsFilePath));
        BackupDirectory = backupDirectory ?? Path.Combine(settingsDirectory ?? Environment.CurrentDirectory, "Backups");
        Directory.CreateDirectory(BackupDirectory);
    }

    /// <summary>Absolute path of the settings file included in every backup.</summary>
    public string SettingsPath { get; }

    /// <summary>Directory holding the ZIP backups created by the terminal UI.</summary>
    public string BackupDirectory { get; }

    /// <summary>Describes the active database without ever exposing credentials.</summary>
    public async Task<TuiDatabaseInfo> GetDatabaseInfoAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
        var connection = context.Database.GetDbConnection();

        if (connection is not SqliteConnection sqlite)
        {
            return new TuiDatabaseInfo(
                connection.GetType().Name,
                "skryto",
                0,
                false,
                "Připojená databáze není SQLite — údaje o připojení se z bezpečnostních důvodů nezobrazují.");
        }

        string dataSource = new SqliteConnectionStringBuilder(sqlite.ConnectionString).DataSource;
        long size = 0;
        try
        {
            if (!string.IsNullOrWhiteSpace(dataSource) && File.Exists(dataSource))
            {
                size = new FileInfo(dataSource).Length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            size = 0;
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return new TuiDatabaseInfo(
            DatabaseProviderKind.Sqlite.ToString(),
            dataSource,
            size,
            true,
            "SQLite: " + dataSource);
    }

    /// <summary>Creates a ZIP backup; an empty destination uses <see cref="BackupDirectory"/>.</summary>
    public async Task<string> CreateBackupAsync(string? destinationPath, CancellationToken cancellationToken)
    {
        string connectionString = GetActiveSqliteConnectionString();
        string fullPath = string.IsNullOrWhiteSpace(destinationPath)
            ? Path.Combine(BackupDirectory, "DiskChecker_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".zip")
            : destinationPath;

        string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        string tempDirectory = Path.Combine(Path.GetTempPath(), "DiskChecker_TuiSnapshot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string snapshotPath = Path.Combine(tempDirectory, DatabaseEntryName);
            await Task.Run(() =>
            {
                // BackupDatabase produces a consistent online snapshot including WAL contents.
                using var source = new SqliteConnection(connectionString);
                using var target = new SqliteConnection(
                    new SqliteConnectionStringBuilder { DataSource = snapshotPath, Pooling = false }.ToString());
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }, cancellationToken).ConfigureAwait(false);

            var metadata = new TuiDataBackupMetadata
            {
                Version = "1.1.0",
                CreatedAt = DateTime.Now,
                DatabasePath = dataSource,
                SettingsPath = SettingsPath,
            };

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            using (var zip = ZipFile.Open(fullPath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshotPath, DatabaseEntryName);
                if (File.Exists(SettingsPath))
                {
                    zip.CreateEntryFromFile(SettingsPath, SettingsEntryName);
                }

                var entry = zip.CreateEntry(MetadataEntryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(JsonSerializer.Serialize(metadata, JsonOptions));
            }

            return fullPath;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    /// <summary>Restores a ZIP backup over the live database, keeping a pre-restore copy.</summary>
    public async Task<TuiDataRestoreOutcome> RestoreAsync(string backupPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("Záložní soubor nebyl nalezen.", backupPath);
        }

        string connectionString = GetActiveSqliteConnectionString();
        string livePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
        string tempDirectory = Path.Combine(Path.GetTempPath(), "DiskChecker_TuiRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            using (var archive = ZipFile.OpenRead(backupPath))
            {
                if (archive.GetEntry(DatabaseEntryName) is null)
                {
                    throw new InvalidDataException("Záloha neobsahuje " + DatabaseEntryName + ".");
                }

                archive.ExtractToDirectory(tempDirectory, overwriteFiles: true);
            }

            string importedPath = Path.Combine(tempDirectory, DatabaseEntryName);
            string importedConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = importedPath,
                Pooling = false,
            }.ToString();

            string preRestorePath = livePath + ".pre_restore_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

            await Task.Run(() =>
            {
                using var source = new SqliteConnection(importedConnectionString);
                source.Open();

                using (var verify = source.CreateCommand())
                {
                    verify.CommandText = "PRAGMA quick_check;";
                    string? result = verify.ExecuteScalar()?.ToString();
                    if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("Obnovovaný soubor SQLite neprošel kontrolou integrity.");
                    }
                }

                // Preserve the currently active data before any replacement.
                using (var current = new SqliteConnection(connectionString))
                using (var backup = new SqliteConnection(
                    new SqliteConnectionStringBuilder { DataSource = preRestorePath, Pooling = false }.ToString()))
                {
                    current.Open();
                    backup.Open();
                    current.BackupDatabase(backup);
                }

                using var destination = new SqliteConnection(connectionString);
                destination.Open();
                source.BackupDatabase(destination);
            }, cancellationToken).ConfigureAwait(false);

            bool settingsRestored = await RestoreSettingsAsync(tempDirectory, cancellationToken).ConfigureAwait(false);

            return new TuiDataRestoreOutcome(
                preRestorePath,
                settingsRestored,
                "Data byly obnovena ze zálohy. Restartujte aplikaci pro načtení obnovených dat.");
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    /// <summary>ZIP backups found in <see cref="BackupDirectory"/>, newest first.</summary>
    public Task<IReadOnlyList<TuiDataBackupInfo>> GetAvailableBackupsAsync(CancellationToken cancellationToken)
    {
        var backups = new List<TuiDataBackupInfo>();
        if (!Directory.Exists(BackupDirectory))
        {
            return Task.FromResult<IReadOnlyList<TuiDataBackupInfo>>(backups);
        }

        foreach (string file in Directory.GetFiles(BackupDirectory, "*.zip"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime createdAt = File.GetLastWriteTime(file);
            string version = "1.0.0";
            try
            {
                using var zip = ZipFile.OpenRead(file);
                var entry = zip.GetEntry(MetadataEntryName);
                if (entry is not null)
                {
                    using var reader = new StreamReader(entry.Open());
                    var metadata = JsonSerializer.Deserialize<TuiDataBackupMetadata>(reader.ReadToEnd());
                    if (metadata is not null)
                    {
                        createdAt = metadata.CreatedAt;
                        version = metadata.Version;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
            {
                // Unreadable archives are still listed so the operator can delete them.
            }

            backups.Add(new TuiDataBackupInfo(file, Path.GetFileName(file), new FileInfo(file).Length, createdAt, version));
        }

        return Task.FromResult<IReadOnlyList<TuiDataBackupInfo>>(
            backups.OrderByDescending(backup => backup.CreatedAt).ToArray());
    }

    /// <summary>Reads the active SQLite connection string, rejecting other providers.</summary>
    public string GetActiveSqliteConnectionString()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
        if (context.Database.GetDbConnection() is not SqliteConnection connection)
        {
            throw new NotSupportedException(
                "Záloha do ZIP podporuje pouze SQLite. Pro PostgreSQL/SQL Server použijte nástroje příslušné databáze.");
        }

        return connection.ConnectionString;
    }

    private async Task<bool> RestoreSettingsAsync(string tempDirectory, CancellationToken cancellationToken)
    {
        string importedSettings = Path.Combine(tempDirectory, SettingsEntryName);
        if (!File.Exists(importedSettings))
        {
            return false;
        }

        // Old backups may point at a different database; keep the active provider/location
        // while restoring unrelated preferences.
        var restored = JsonNode.Parse(await File.ReadAllTextAsync(importedSettings, cancellationToken).ConfigureAwait(false))?.AsObject();
        if (restored is null)
        {
            return false;
        }

        if (File.Exists(SettingsPath))
        {
            var current = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath, cancellationToken).ConfigureAwait(false))?.AsObject();
            if (current is not null)
            {
                foreach (string key in new[] { "DatabaseProvider", "DatabaseConnectionString" })
                {
                    if (current.TryGetPropertyValue(key, out var value))
                    {
                        restored[key] = value?.DeepClone();
                    }
                    else
                    {
                        restored.Remove(key);
                    }
                }
            }
        }

        await File.WriteAllTextAsync(SettingsPath, restored.ToJsonString(JsonOptions), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary directories are best effort cleanup.
        }
    }
}
