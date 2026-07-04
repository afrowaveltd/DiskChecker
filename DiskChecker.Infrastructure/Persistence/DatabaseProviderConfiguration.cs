using DiskChecker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Infrastructure.Persistence;

public static class DatabaseProviderConfiguration
{
    public static void Configure(DbContextOptionsBuilder options, DatabaseStorageSettings settings)
    {
        settings ??= DatabaseStorageSettings.Default;
        var connectionString = string.IsNullOrWhiteSpace(settings.ConnectionString)
            ? DatabaseStorageSettings.Default.ConnectionString
            : settings.ConnectionString!;

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
                options.UseSqlite(connectionString);
                break;
        }
    }
}
