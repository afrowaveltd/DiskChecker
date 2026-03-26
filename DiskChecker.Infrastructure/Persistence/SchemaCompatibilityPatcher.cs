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

        // Support both legacy and current schema names.
        var testsTable = TableExists(dbContext, "Tests")
            ? "Tests"
            : TableExists(dbContext, "TestRecords")
                ? "TestRecords"
                : null;

        if (!string.IsNullOrWhiteSpace(testsTable))
        {
            EnsureColumn(dbContext, testsTable, "IsCompleted");
            EnsureColumn(dbContext, testsTable, "IsArchived");
            EnsureColumn(dbContext, testsTable, "ArchiveBatchId");
            // Older database versions may be missing error-related columns — ensure they exist
            EnsureColumn(dbContext, testsTable, "ErrorCount");
            EnsureColumn(dbContext, testsTable, "Errors");
        }

        if (TableExists(dbContext, "TestSessions"))
        {
            EnsureColumn(dbContext, "TestSessions", "SmartBeforeJson");
            EnsureColumn(dbContext, "TestSessions", "SmartAfterJson");
        }

        if (TableExists(dbContext, "DiskCards"))
        {
            EnsureColumn(dbContext, "DiskCards", "PowerOnHours");
            EnsureColumn(dbContext, "DiskCards", "PowerCycleCount");
            EnsureIndex(dbContext, "IX_DiskCards_DevicePath", "DiskCards", "DevicePath");
        }
    }

    private static void EnsureColumn(DiskCheckerDbContext dbContext, string tableName, string columnName)
    {
        if (!TableExists(dbContext, tableName) || ColumnExists(dbContext, tableName, columnName))
        {
            return;
        }

        if (tableName == "Tests" && columnName == "IsCompleted")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tests ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 1;");
            return;
        }

        if (tableName == "TestRecords" && columnName == "IsCompleted")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestRecords ADD COLUMN IsCompleted INTEGER NOT NULL DEFAULT 1;");
            return;
        }

        if (tableName == "Tests" && columnName == "IsArchived")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tests ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;");
            return;
        }

        if (tableName == "TestRecords" && columnName == "IsArchived")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestRecords ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;");
            return;
        }

        if (tableName == "Tests" && columnName == "ArchiveBatchId")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tests ADD COLUMN ArchiveBatchId TEXT NULL;");
            return;
        }

        if (tableName == "TestRecords" && columnName == "ArchiveBatchId")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestRecords ADD COLUMN ArchiveBatchId TEXT NULL;");
            return;
        }

        if (tableName == "Tests" && columnName == "ErrorCount")
        {
            // ErrorCount introduced later as integer counter of distinct error groups
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tests ADD COLUMN ErrorCount INTEGER NOT NULL DEFAULT 0;");
            return;
        }

        if (tableName == "TestRecords" && columnName == "ErrorCount")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestRecords ADD COLUMN ErrorCount INTEGER NOT NULL DEFAULT 0;");
            return;
        }

        if (tableName == "Tests" && columnName == "Errors")
        {
            // Older schemas may also lack the Errors column (legacy), add it as integer default 0
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE Tests ADD COLUMN Errors INTEGER NOT NULL DEFAULT 0;");
            return;
        }

        if (tableName == "TestRecords" && columnName == "Errors")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestRecords ADD COLUMN Errors INTEGER NOT NULL DEFAULT 0;");
            return;
        }

        if (tableName == "TestSessions" && columnName == "SmartBeforeJson")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestSessions ADD COLUMN SmartBeforeJson TEXT NULL;");
            return;
        }

        if (tableName == "TestSessions" && columnName == "SmartAfterJson")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestSessions ADD COLUMN SmartAfterJson TEXT NULL;");
            return;
        }

        if (tableName == "DiskCards" && columnName == "PowerOnHours")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCards ADD COLUMN PowerOnHours INTEGER NULL;");
            return;
        }

        if (tableName == "DiskCards" && columnName == "PowerCycleCount")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCards ADD COLUMN PowerCycleCount INTEGER NULL;");
            return;
        }
    }

    private static bool TableExists(DiskCheckerDbContext dbContext, string tableName)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        var result = command.ExecuteScalar();
        return result != null && result != DBNull.Value;
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

    private static void EnsureIndex(DiskCheckerDbContext dbContext, string indexName, string tableName, string columnName)
    {
        if (!TableExists(dbContext, tableName))
        {
            return;
        }

        if (!IsSafeSqlIdentifier(indexName) || !IsSafeSqlIdentifier(tableName) || !IsSafeSqlIdentifier(columnName))
        {
            return;
        }

        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = $name LIMIT 1;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = indexName;
        command.Parameters.Add(parameter);

        var exists = command.ExecuteScalar() != null;
        if (!exists)
        {
            using var createCommand = connection.CreateCommand();
            createCommand.CommandText = "CREATE INDEX IF NOT EXISTS " + indexName + " ON " + tableName + "(" + columnName + ");";
            createCommand.ExecuteNonQuery();
        }
    }

    private static bool IsSafeSqlIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        foreach (var ch in identifier)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
