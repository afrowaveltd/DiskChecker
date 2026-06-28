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
            EnsureColumn(dbContext, "TestSessions", "ChartImagePath");
            EnsureColumn(dbContext, "TestSessions", "CertificateId");
            EnsureColumn(dbContext, "TestSessions", "SeekResultsJson");
            EnsureColumn(dbContext, "TestSessions", "Sanitize1ResultJson");
            EnsureColumn(dbContext, "TestSessions", "Sanitize2ResultJson");
        }
        if (TableExists(dbContext, "DiskCards"))
        {
            EnsureColumn(dbContext, "DiskCards", "PowerOnHours");
            EnsureColumn(dbContext, "DiskCards", "PowerCycleCount");
            EnsureIndex(dbContext, "IX_DiskCards_DevicePath", "DiskCards", "DevicePath");
        }

        if (TableExists(dbContext, "EmailSettings"))
        {
            EnsureColumn(dbContext, "EmailSettings", "IncludeCertificateAttachment");
        }

        if (TableExists(dbContext, "DiskCertificates"))
        {
            // Columns added when absolute destructive test support was introduced
            EnsureColumn(dbContext, "DiskCertificates", "TestSessionId");
            EnsureColumn(dbContext, "DiskCertificates", "TestDuration");
            EnsureColumn(dbContext, "DiskCertificates", "AvgWriteSpeed");
            EnsureColumn(dbContext, "DiskCertificates", "MaxWriteSpeed");
            EnsureColumn(dbContext, "DiskCertificates", "AvgReadSpeed");
            EnsureColumn(dbContext, "DiskCertificates", "MaxReadSpeed");
            EnsureColumn(dbContext, "DiskCertificates", "ErrorCount");
            EnsureColumn(dbContext, "DiskCertificates", "SmartPassed");
            EnsureColumn(dbContext, "DiskCertificates", "PowerOnHours");
            EnsureColumn(dbContext, "DiskCertificates", "PowerCycles");
            EnsureColumn(dbContext, "DiskCertificates", "ReallocatedSectors");
            EnsureColumn(dbContext, "DiskCertificates", "PendingSectors");
            EnsureColumn(dbContext, "DiskCertificates", "SanitizationPerformed");
            EnsureColumn(dbContext, "DiskCertificates", "DataVerified");
            EnsureColumn(dbContext, "DiskCertificates", "Firmware");
            EnsureColumn(dbContext, "DiskCertificates", "Interface");
            EnsureColumn(dbContext, "DiskCertificates", "ValidUntil");
            EnsureColumn(dbContext, "DiskCertificates", "PdfGenerated");
            EnsureColumn(dbContext, "DiskCertificates", "ChartImagePath");
            EnsureColumn(dbContext, "DiskCertificates", "WriteProfilePoints");
            EnsureColumn(dbContext, "DiskCertificates", "ReadProfilePoints");
            EnsureColumn(dbContext, "DiskCertificates", "SeekAvgLatencyMs");
            EnsureColumn(dbContext, "DiskCertificates", "SeekMinLatencyMs");
            EnsureColumn(dbContext, "DiskCertificates", "SeekMaxLatencyMs");
            EnsureColumn(dbContext, "DiskCertificates", "SeekStdDevLatencyMs");
            EnsureColumn(dbContext, "DiskCertificates", "SeekP95LatencyMs");
            EnsureColumn(dbContext, "DiskCertificates", "SeekTestSummary");
            EnsureColumn(dbContext, "DiskCertificates", "Sanitize1AvgWriteMBps");
            EnsureColumn(dbContext, "DiskCertificates", "Sanitize2AvgWriteMBps");
            EnsureColumn(dbContext, "DiskCertificates", "WriteSpeedChangePercent");
            EnsureColumn(dbContext, "DiskCertificates", "Sanitize1AvgReadMBps");
            EnsureColumn(dbContext, "DiskCertificates", "Sanitize2AvgReadMBps");
            EnsureColumn(dbContext, "DiskCertificates", "ReadSpeedChangePercent");
            EnsureColumn(dbContext, "DiskCertificates", "Sanitize1Errors");
            EnsureColumn(dbContext, "DiskCertificates", "Sanitize2Errors");
            EnsureColumn(dbContext, "DiskCertificates", "SmartDeltaSummary");
            EnsureColumn(dbContext, "DiskCertificates", "Recommended");
            EnsureColumn(dbContext, "DiskCertificates", "RecommendationNotes");
            EnsureColumn(dbContext, "DiskCertificates", "Status");
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
        if (tableName == "TestSessions" && columnName == "ChartImagePath")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestSessions ADD COLUMN ChartImagePath TEXT NULL;");
            return;
        }

        if (tableName == "TestSessions" && columnName == "CertificateId")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestSessions ADD COLUMN CertificateId INTEGER NULL;");
            return;
        }

        if (tableName == "TestSessions" && columnName == "SeekResultsJson")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestSessions ADD COLUMN SeekResultsJson TEXT NULL;");
            return;
        }

        if (tableName == "TestSessions" && columnName == "Sanitize1ResultJson")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestSessions ADD COLUMN Sanitize1ResultJson TEXT NULL;");
            return;
        }

        if (tableName == "TestSessions" && columnName == "Sanitize2ResultJson")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestSessions ADD COLUMN Sanitize2ResultJson TEXT NULL;");
            return;
        }

        if (tableName == "TestSessions" && columnName == "AnomaliesJson")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE TestSessions ADD COLUMN AnomaliesJson TEXT NULL;");
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

        if (tableName == "EmailSettings" && columnName == "IncludeCertificateAttachment")
        {
            dbContext.Database.ExecuteSqlRaw("ALTER TABLE EmailSettings ADD COLUMN IncludeCertificateAttachment INTEGER NOT NULL DEFAULT 1;");
            return;
        }

        // ── DiskCertificates columns ──
        // These columns were added when absolute destructive test support was
        // introduced.  Older databases created before this feature need them added
        // via ALTER TABLE so that SaveChangesAsync does not throw.

        if (tableName != "DiskCertificates")
        {
            return;
        }

        switch (columnName)
        {
            case "TestSessionId":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN TestSessionId INTEGER NOT NULL DEFAULT 0;");
                return;
            case "TestDuration":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN TestDuration TEXT NULL;");
                return;
            case "AvgWriteSpeed":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN AvgWriteSpeed REAL NOT NULL DEFAULT 0;");
                return;
            case "MaxWriteSpeed":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN MaxWriteSpeed REAL NOT NULL DEFAULT 0;");
                return;
            case "AvgReadSpeed":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN AvgReadSpeed REAL NOT NULL DEFAULT 0;");
                return;
            case "MaxReadSpeed":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN MaxReadSpeed REAL NOT NULL DEFAULT 0;");
                return;
            case "ErrorCount":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN ErrorCount INTEGER NOT NULL DEFAULT 0;");
                return;
            case "SmartPassed":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN SmartPassed INTEGER NOT NULL DEFAULT 0;");
                return;
            case "PowerOnHours":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN PowerOnHours INTEGER NOT NULL DEFAULT 0;");
                return;
            case "PowerCycles":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN PowerCycles INTEGER NOT NULL DEFAULT 0;");
                return;
            case "ReallocatedSectors":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN ReallocatedSectors INTEGER NOT NULL DEFAULT 0;");
                return;
            case "PendingSectors":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN PendingSectors INTEGER NOT NULL DEFAULT 0;");
                return;
            case "SanitizationPerformed":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN SanitizationPerformed INTEGER NOT NULL DEFAULT 0;");
                return;
            case "DataVerified":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN DataVerified INTEGER NOT NULL DEFAULT 0;");
                return;
            case "Firmware":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN Firmware TEXT NULL;");
                return;
            case "Interface":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN Interface TEXT NULL;");
                return;
            case "ValidUntil":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN ValidUntil TEXT NULL;");
                return;
            case "PdfGenerated":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN PdfGenerated INTEGER NOT NULL DEFAULT 0;");
                return;
            case "ChartImagePath":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN ChartImagePath TEXT NULL;");
                return;
            case "WriteProfilePoints":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN WriteProfilePoints TEXT NULL;");
                return;
            case "ReadProfilePoints":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN ReadProfilePoints TEXT NULL;");
                return;
            case "SeekAvgLatencyMs":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN SeekAvgLatencyMs REAL NULL;");
                return;
            case "SeekMinLatencyMs":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN SeekMinLatencyMs REAL NULL;");
                return;
            case "SeekMaxLatencyMs":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN SeekMaxLatencyMs REAL NULL;");
                return;
            case "SeekStdDevLatencyMs":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN SeekStdDevLatencyMs REAL NULL;");
                return;
            case "SeekP95LatencyMs":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN SeekP95LatencyMs REAL NULL;");
                return;
            case "SeekTestSummary":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN SeekTestSummary TEXT NULL;");
                return;
            case "Sanitize1AvgWriteMBps":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN Sanitize1AvgWriteMBps REAL NULL;");
                return;
            case "Sanitize2AvgWriteMBps":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN Sanitize2AvgWriteMBps REAL NULL;");
                return;
            case "WriteSpeedChangePercent":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN WriteSpeedChangePercent REAL NULL;");
                return;
            case "Sanitize1AvgReadMBps":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN Sanitize1AvgReadMBps REAL NULL;");
                return;
            case "Sanitize2AvgReadMBps":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN Sanitize2AvgReadMBps REAL NULL;");
                return;
            case "ReadSpeedChangePercent":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN ReadSpeedChangePercent REAL NULL;");
                return;
            case "Sanitize1Errors":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN Sanitize1Errors INTEGER NULL;");
                return;
            case "Sanitize2Errors":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN Sanitize2Errors INTEGER NULL;");
                return;
            case "SmartDeltaSummary":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN SmartDeltaSummary TEXT NULL;");
                return;
            case "Recommended":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN Recommended INTEGER NOT NULL DEFAULT 0;");
                return;
            case "RecommendationNotes":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN RecommendationNotes TEXT NULL;");
                return;
            case "Status":
                dbContext.Database.ExecuteSqlRaw("ALTER TABLE DiskCertificates ADD COLUMN Status INTEGER NOT NULL DEFAULT 0;");
                return;
            default:
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
