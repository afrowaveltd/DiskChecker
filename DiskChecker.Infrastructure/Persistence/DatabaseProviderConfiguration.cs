using DiskChecker.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace DiskChecker.Infrastructure.Persistence;

public static class DatabaseProviderConfiguration
{
    public static void Configure(DbContextOptionsBuilder options, DatabaseStorageSettings settings)
    {
        settings ??= DatabaseStorageSettings.Default;
        var connectionString = string.IsNullOrWhiteSpace(settings.ConnectionString)
            ? DatabaseStorageSettings.Default.ConnectionString
                ?? throw new InvalidOperationException("Default database connection string is missing.")
            : settings.ConnectionString;

        switch (settings.Provider)
        {
            case DatabaseProviderKind.PostgreSql:
                options.UseNpgsql(connectionString);
                break;
            case DatabaseProviderKind.SqlServer:
                options.UseSqlServer(connectionString);
                break;
            case DatabaseProviderKind.Sqlite:
            default:
                options.UseSqlite(NormalizeSqliteConnectionString(connectionString));
                break;
        }
    }

    /// <summary>
    /// Anchor file-backed SQLite paths to a stable absolute location. Relative
    /// SQLite paths otherwise change databases when the process CWD changes.
    /// </summary>
    public static string NormalizeSqliteConnectionString(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (!string.IsNullOrWhiteSpace(dataSource) && dataSource != ":memory:" &&
            !dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
            !Path.IsPathRooted(dataSource))
        {
            builder.DataSource = Path.GetFullPath(dataSource);
        }
        return builder.ToString();
    }
}
