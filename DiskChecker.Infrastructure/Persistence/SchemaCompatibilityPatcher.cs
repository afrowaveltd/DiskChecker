using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// Applies backward-compatible schema upgrades for existing SQLite databases.
/// </summary>
public static class SchemaCompatibilityPatcher
{
    /// <summary>
    /// Ensures required columns exist for runtime compatibility.
    /// </summary>
    /// <param name="dbContext">Database context.</param>
    public static void Apply(DiskCheckerDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        if (!dbContext.Database.IsSqlite())
        {
            return;
        }

        EnsureColumn(dbContext, "Tests", "IsCompleted");
        EnsureColumn(dbContext, "Tests", "IsArchived");
        EnsureColumn(dbContext, "Tests", "ArchiveBatchId");
        // Older database versions may be missing error-related columns — ensure they exist
        EnsureColumn(dbContext, "Tests", "ErrorCount");
        EnsureColumn(dbContext, "Tests", "Errors");
    }

    private static void EnsureColumn(DiskCheckerDbContext dbContext, string tableName, string columnName)
    {
        if (ColumnExists(dbContext, tableName, columnName))
        {
            return;
        }

        if (tableName == "Tests" && columnName == "IsCompleted")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tests ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 1;");
            return;
        }

        if (tableName == "Tests" && columnName == "IsArchived")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tests ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;");
            return;
        }

        if (tableName == "Tests" && columnName == "ArchiveBatchId")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tests ADD COLUMN ArchiveBatchId TEXT NULL;");
        }

        if (tableName == "Tests" && columnName == "ErrorCount")
        {
            // ErrorCount introduced later as integer counter of distinct error groups
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tests ADD COLUMN ErrorCount INTEGER NOT NULL DEFAULT 0;");
            return;
        }

        if (tableName == "Tests" && columnName == "Errors")
        {
            // Older schemas may also lack the Errors column (legacy), add it as integer default 0
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tests ADD COLUMN Errors INTEGER NOT NULL DEFAULT 0;");
            return;
        }
    }

    private static bool ColumnExists(DiskCheckerDbContext dbContext, string tableName, string columnName)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var existing = reader.GetString(1);
            if (string.Equals(existing, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
