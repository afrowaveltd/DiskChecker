using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.UI.Avalonia.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiskChecker.Tests;

public class DatabaseStorageConsistencyTests
{
    [Fact]
    public async Task DatabaseSettings_SurviveServiceRestartWithSameAbsolutePath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DiskCheckerDbTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var first = new SettingsService(folder);
            var initial = await first.GetDatabaseStorageSettingsAsync();
            var defaultPath = new SqliteConnectionStringBuilder(initial.ConnectionString ?? throw new InvalidOperationException("Missing SQLite connection string")).DataSource;
            Assert.Equal(Path.GetFullPath(Path.Combine(folder, "DiskChecker.db")), defaultPath);

            var explicitPath = Path.Combine(folder, "other", "chosen.db");
            await first.SetDatabaseStorageSettingsAsync(new DatabaseStorageSettings
            {
                Provider = DatabaseProviderKind.Sqlite,
                ConnectionString = new SqliteConnectionStringBuilder { DataSource = explicitPath }.ToString()
            });

            var restarted = new SettingsService(folder);
            var restored = await restarted.GetDatabaseStorageSettingsAsync();
            Assert.Equal(DatabaseProviderKind.Sqlite, restored.Provider);
            Assert.Equal(explicitPath, new SqliteConnectionStringBuilder(restored.ConnectionString ?? throw new InvalidOperationException("Restored SQLite connection string missing")).DataSource);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ChangingDatabaseSettings_DoesNotChangeStartupDatabase()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DiskCheckerDbTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var settings = new SettingsService(folder);
            var original = settings.StartupDatabaseStorageSettings.ConnectionString;
            await settings.SetDatabaseStorageSettingsAsync(new DatabaseStorageSettings
            {
                Provider = DatabaseProviderKind.Sqlite,
                ConnectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(folder, "future.db")
                }.ToString()
            });

            Assert.Equal(original, settings.StartupDatabaseStorageSettings.ConnectionString);
            var restarted = new SettingsService(folder);
            Assert.NotEqual(original, restarted.StartupDatabaseStorageSettings.ConnectionString);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task DatabaseSettings_PersistNonSqliteBackend()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DiskCheckerDbTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var settings = new SettingsService(folder);
            await settings.SetDatabaseStorageSettingsAsync(new DatabaseStorageSettings
            {
                Provider = DatabaseProviderKind.PostgreSql,
                ConnectionString = "Host=localhost;Database=testdb;Username=test;Password=dummy"
            });

            var reopened = await new SettingsService(folder).GetDatabaseStorageSettingsAsync();
            Assert.Equal(DatabaseProviderKind.PostgreSql, reopened.Provider);
            Assert.Contains("Database=testdb", reopened.ConnectionString!);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Backup_UsesActiveDbNotDifferentFileBesideExecutable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DiskCheckerBackupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var active = Path.Combine(folder, "actual.db");
            var other = Path.Combine(folder, "unused.db");
            CreateMarkerDb(active, "ACTIVE");
            CreateMarkerDb(other, "OTHER");

            var settings = new SettingsService(folder);
            var cs = new SqliteConnectionStringBuilder { DataSource = active, Pooling = false }.ToString();
            await settings.SetDatabaseStorageSettingsAsync(new DatabaseStorageSettings
            {
                Provider = DatabaseProviderKind.Sqlite, ConnectionString = cs
            });

            var services = new ServiceCollection();
            services.AddSingleton(settings);
            services.AddDbContext<DiskCheckerDbContext>(o => o.UseSqlite(cs));
            services.AddScoped<BackupService>();
            var zipPath = Path.Combine(folder, "backup.zip");
            // Release the provider and its DbContext before deleting the test
            // directory; Windows cannot delete SQLite files held by a connection.
            using (var provider = services.BuildServiceProvider())
            using (var scope = provider.CreateScope())
            {
                var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
                await backup.CreateBackupAsync(zipPath);
            }

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.GetEntry("DiskChecker.db");
                Assert.NotNull(entry);
                var extracted = Path.Combine(folder, "extracted.db");
                entry!.ExtractToFile(extracted);
                Assert.Equal("ACTIVE", ReadMarker(extracted));
            }
            Assert.Equal("OTHER", ReadMarker(other));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Backup_AcceptsExistingDestinationFolderAndPublishesCompleteArchives()
    {
        var folder = Directory.CreateTempSubdirectory("dc-backup-folder-").FullName;
        try
        {
            var active = Path.Combine(folder, "active.db");
            CreateMarkerDb(active, "ACTIVE");
            var settings = new SettingsService(folder);
            var cs = new SqliteConnectionStringBuilder { DataSource = active, Pooling = false }.ToString();
            var services = new ServiceCollection();
            services.AddSingleton(settings);
            services.AddDbContext<DiskCheckerDbContext>(o => o.UseSqlite(cs));
            services.AddScoped<BackupService>();
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
            var destination = Directory.CreateDirectory(Path.Combine(folder, "destination")).FullName;

            var first = await backup.CreateBackupAsync(destination);
            var second = await backup.CreateBackupAsync(destination);
            Assert.NotEqual(first, second);
            Assert.Equal(destination, Path.GetDirectoryName(first));
            Assert.Equal(destination, Path.GetDirectoryName(second));
            using var archive = ZipFile.OpenRead(first);
            Assert.NotNull(archive.GetEntry("DiskChecker.db"));
            Assert.NotNull(archive.GetEntry("backup_metadata.json"));
            Assert.Empty(Directory.GetFiles(destination, "*.tmp"));
            await Assert.ThrowsAsync<IOException>(() => backup.CreateBackupAsync(first));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Restore_MalformedSettingsDoesNotReplaceLiveDatabase()
    {
        var folder = Directory.CreateTempSubdirectory("dc-restore-validation-").FullName;
        try
        {
            var active = Path.Combine(folder, "active.db");
            var incoming = Path.Combine(folder, "incoming.db");
            CreateMarkerDb(active, "ACTIVE");
            CreateMarkerDb(incoming, "INCOMING");
            var settings = new SettingsService(folder);
            var cs = new SqliteConnectionStringBuilder { DataSource = active, Pooling = false }.ToString();
            var services = new ServiceCollection();
            services.AddSingleton(settings);
            services.AddDbContext<DiskCheckerDbContext>(o => o.UseSqlite(cs));
            services.AddScoped<BackupService>();
            var zipPath = Path.Combine(folder, "invalid.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(incoming, "DiskChecker.db");
                using var writer = new StreamWriter(archive.CreateEntry("settings.json").Open());
                writer.Write("[]");
            }

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
            await Assert.ThrowsAsync<InvalidDataException>(() => backup.RestoreBackupAsync(zipPath));
            Assert.Equal("ACTIVE", ReadMarker(active));
            Assert.Empty(Directory.GetFiles(folder, "*.pre_restore_*"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_SettingsPublicationDeterminesWhetherIncomingDatabaseIsKept(bool failPublication)
    {
        var folder = Directory.CreateTempSubdirectory("dc-restore-compensation-").FullName;
        try
        {
            var active = Path.Combine(folder, "active.db");
            var incoming = Path.Combine(folder, "incoming.db");
            CreateDiagnosticMarkerDb(active, "ACTIVE");
            CreateDiagnosticMarkerDb(incoming, "INCOMING");
            var settings = new SettingsService(folder);
            var cs = new SqliteConnectionStringBuilder { DataSource = active, Pooling = false }.ToString();
            await settings.SetDatabaseStorageSettingsAsync(new DatabaseStorageSettings
            {
                Provider = DatabaseProviderKind.Sqlite, ConnectionString = cs
            });
            var originalSettings = await File.ReadAllTextAsync(settings.SettingsFilePath, TestContext.Current.CancellationToken);
            var services = new ServiceCollection();
            services.AddDbContext<DiskCheckerDbContext>(o => o.UseSqlite(cs));
            using var provider = services.BuildServiceProvider();

            var zipPath = Path.Combine(folder, "restore.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(incoming, "DiskChecker.db");
                using var writer = new StreamWriter(archive.CreateEntry("settings.json").Open());
                writer.Write("{\"RestoreMarker\":\"NEW\",\"DatabaseConnectionString\":\"Data Source=wrong.db\"}");
            }

            BackupService backup = failPublication
                ? new FailingSettingsPublicationBackupService(provider.GetRequiredService<IServiceScopeFactory>(), settings, active)
                : new BackupService(provider.GetRequiredService<IServiceScopeFactory>(), settings);

            if (failPublication)
            {
                var error = await Assert.ThrowsAsync<IOException>(() => backup.RestoreBackupAsync(zipPath));
                Assert.Contains("original database was recovered", error.Message);
                Assert.Equal("Injected settings publication failure.", Assert.IsType<IOException>(error.InnerException).Message);
                Assert.Equal("ACTIVE", ReadMarker(active));
                Assert.Equal(originalSettings, await File.ReadAllTextAsync(settings.SettingsFilePath, TestContext.Current.CancellationToken));
            }
            else
            {
                await backup.RestoreBackupAsync(zipPath);
                Assert.Equal("INCOMING", ReadMarker(active));
                var restored = await File.ReadAllTextAsync(settings.SettingsFilePath, TestContext.Current.CancellationToken);
                Assert.Contains("RestoreMarker", restored);
                Assert.DoesNotContain("wrong.db", restored);
            }

            var snapshot = Assert.Single(Directory.GetFiles(folder, "active.db.pre_restore_*"));
            Assert.Equal("ACTIVE", ReadMarker(snapshot));
            Assert.False(File.Exists(snapshot + "-wal"));
            Assert.False(File.Exists(snapshot + "-shm"));
            using (var recovered = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = snapshot, Mode = SqliteOpenMode.ReadOnly, Pooling = false
            }.ToString()))
            {
                recovered.Open();
                using var journal = recovered.CreateCommand();
                journal.CommandText = "PRAGMA journal_mode;";
                Assert.Equal("delete", journal.ExecuteScalar()?.ToString());
            }
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_RejectsUnrelatedOrInconsistentDatabaseBeforeReplacement(bool crossDisk)
    {
        var folder = Directory.CreateTempSubdirectory("dc-restore-schema-").FullName;
        try
        {
            var active = Path.Combine(folder, "active.db");
            var incoming = Path.Combine(folder, "incoming.db");
            CreateDiagnosticMarkerDb(active, "ACTIVE");
            if (crossDisk)
            {
                CreateDiagnosticMarkerDb(incoming, "INCOMING");
                var incomingCs = new SqliteConnectionStringBuilder { DataSource = incoming, Pooling = false }.ToString();
                await using var context = new DiskCheckerDbContext(
                    new DbContextOptionsBuilder<DiskCheckerDbContext>().UseSqlite(incomingCs).Options);
                var repo = new DiskCardRepository(context);
                var a = await repo.CreateAsync(new DiskCard { SerialNumber = "DISK-A-123", ModelName = "A" });
                var b = await repo.CreateAsync(new DiskCard { SerialNumber = "DISK-B-456", ModelName = "B" });
                var session = await repo.CreateTestSessionAsync(new TestSession { DiskCardId = a.Id });
                var certificate = await repo.CreateCertificateAsync(new DiskCertificate
                {
                    DiskCardId = a.Id, TestSessionId = session.Id, CertificateNumber = "RESTORE-TEST"
                });
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE DiskCertificates SET DiskCardId = {0} WHERE Id = {1}", b.Id, certificate.Id);
            }
            else
            {
                CreateMarkerDb(incoming, "INCOMING");
            }

            var settings = new SettingsService(folder);
            var cs = new SqliteConnectionStringBuilder { DataSource = active, Pooling = false }.ToString();
            var services = new ServiceCollection();
            services.AddDbContext<DiskCheckerDbContext>(o => o.UseSqlite(cs));
            using var provider = services.BuildServiceProvider();
            var backup = new BackupService(provider.GetRequiredService<IServiceScopeFactory>(), settings);
            var zipPath = Path.Combine(folder, "invalid.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                archive.CreateEntryFromFile(incoming, "DiskChecker.db");

            await Assert.ThrowsAsync<InvalidDataException>(() => backup.RestoreBackupAsync(zipPath));
            Assert.Equal("ACTIVE", ReadMarker(active));
            Assert.Empty(Directory.GetFiles(folder, "*.pre_restore_*"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void CreateDiagnosticMarkerDb(string path, string marker)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using (var context = new DiskCheckerDbContext(
            new DbContextOptionsBuilder<DiskCheckerDbContext>().UseSqlite(cs).Options))
            context.Database.EnsureCreated();
        CreateMarkerDb(path, marker);
    }

    private sealed class FailingSettingsPublicationBackupService(
        IServiceScopeFactory scopeFactory, SettingsService settings, string activePath)
        : BackupService(scopeFactory, settings)
    {
        protected override void PublishRestoredSettings(string temporaryPath, string destinationPath)
        {
            // Verify failure is injected after SQLite has actually been replaced.
            if (ReadMarker(activePath) != "INCOMING")
                throw new InvalidOperationException("The test did not reach the publication boundary.");
            throw new IOException("Injected settings publication failure.");
        }
    }

    private static void CreateMarkerDb(string path, string marker)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        conn.Open();
        using var command = conn.CreateCommand();
        command.CommandText = "CREATE TABLE Marker (Value TEXT NOT NULL); INSERT INTO Marker (Value) VALUES ($value);";
        command.Parameters.AddWithValue("$value", marker);
        command.ExecuteNonQuery();
    }

    private static string ReadMarker(string path)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        conn.Open();
        using var command = conn.CreateCommand();
        command.CommandText = "SELECT Value FROM Marker LIMIT 1;";
        return (string)command.ExecuteScalar()!;
    }
}
