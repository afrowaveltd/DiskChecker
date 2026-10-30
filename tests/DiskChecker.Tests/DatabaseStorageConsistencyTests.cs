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
            var defaultPath = new SqliteConnectionStringBuilder(initial.ConnectionString).DataSource;
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
            Assert.Equal(explicitPath, new SqliteConnectionStringBuilder(restored.ConnectionString).DataSource);
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
            var cs = new SqliteConnectionStringBuilder { DataSource = active }.ToString();
            await settings.SetDatabaseStorageSettingsAsync(new DatabaseStorageSettings
            {
                Provider = DatabaseProviderKind.Sqlite, ConnectionString = cs
            });

            var services = new ServiceCollection();
            services.AddSingleton(settings);
            services.AddDbContext<DiskCheckerDbContext>(o => o.UseSqlite(cs));
            services.AddScoped<BackupService>();
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
            var zipPath = Path.Combine(folder, "backup.zip");

            await backup.CreateBackupAsync(zipPath);

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

    private static void CreateMarkerDb(string path, string marker)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        conn.Open();
        using var command = conn.CreateCommand();
        command.CommandText = "CREATE TABLE Marker (Value TEXT NOT NULL); INSERT INTO Marker (Value) VALUES ($value);";
        command.Parameters.AddWithValue("$value", marker);
        command.ExecuteNonQuery();
    }

    private static string ReadMarker(string path)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        conn.Open();
        using var command = conn.CreateCommand();
        command.CommandText = "SELECT Value FROM Marker LIMIT 1;";
        return (string)command.ExecuteScalar()!;
    }
}
