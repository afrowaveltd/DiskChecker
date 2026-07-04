namespace DiskChecker.Core.Models;

public enum DatabaseProviderKind
{
    Sqlite = 0,
    PostgreSql = 1,
    SqlServer = 2
}

public class DatabaseStorageSettings
{
    public DatabaseProviderKind Provider { get; set; } = DatabaseProviderKind.Sqlite;
    public string? ConnectionString { get; set; }

    public static DatabaseStorageSettings Default => new()
    {
        Provider = DatabaseProviderKind.Sqlite,
        ConnectionString = "Data Source=DiskChecker.db"
    };
}
