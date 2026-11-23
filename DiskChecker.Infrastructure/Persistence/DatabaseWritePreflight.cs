using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// The result of a non-destructive persistence preflight. A successful probe is
/// only a point-in-time check, not a guarantee against later I/O failures.
/// </summary>
public sealed record DatabaseWritePreflightResult(bool IsReady, string Detail);

/// <summary>
/// Proves the configured SQLite database can perform a real transactional write.
/// The schema and inserted row are rolled back. No physical disk is modified.
/// Other EF providers are currently limited to connectivity checks.
/// </summary>
public static class DatabaseWritePreflight
{
    private const long MinimumFreeBytes = 128L * 1024 * 1024;
    private const long MinimumSqliteHeadroomBytes = 32L * 1024 * 1024;

    /// <summary>
    /// Checks accessible disk space, SQLite page quota and rollback-only
    /// CREATE TABLE/INSERT on the actual DbContext connection.
    /// </summary>
    public static async Task<DatabaseWritePreflightResult> CheckAsync(
        DiskCheckerDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            if (!context.Database.IsSqlite())
            {
                var connected = await context.Database.CanConnectAsync(cancellationToken);
                return connected
                    ? new(true, "Non-SQLite provider is reachable (write access not verified).")
                    : new(false, "The configured database provider could not be reached.");
            }

            var connection = context.Database.GetDbConnection();
            var source = (connection as SqliteConnection)?.DataSource;
            if (!string.IsNullOrWhiteSpace(source) &&
                !string.Equals(source, ":memory:", StringComparison.OrdinalIgnoreCase) &&
                !source.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var dbPath = Path.GetFullPath(source);
                var root = Path.GetPathRoot(dbPath);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var volume = new DriveInfo(root);
                    if (volume.IsReady && volume.AvailableFreeSpace < MinimumFreeBytes)
                    {
                        return new(false,
                            $"SQLite database volume for {dbPath} has only " +
                            $"{volume.AvailableFreeSpace / (1024d * 1024):F0} MiB free; " +
                            "at least 128 MiB is required for a new destructive test.");
                    }
                }
            }

            var openedByUs = connection.State != ConnectionState.Open;
            try
            {
                if (openedByUs)
                    await context.Database.OpenConnectionAsync(cancellationToken);

                var usedPages = await ReadPragmaLongAsync(connection, "page_count", cancellationToken);
                var maxPages = await ReadPragmaLongAsync(connection, "max_page_count", cancellationToken);
                var pageSize = await ReadPragmaLongAsync(connection, "page_size", cancellationToken);
                if (usedPages >= 0 && maxPages > 0 && pageSize > 0 &&
                    maxPages > usedPages &&
                    (maxPages - usedPages) * pageSize < MinimumSqliteHeadroomBytes)
                {
                    return new(false,
                        "SQLite max_page_count leaves less than 32 MiB of database " +
                        "growth capacity. Increase the configured quota before sanitization.");
                }
                if (usedPages >= maxPages && maxPages > 0)
                    return new(false, "SQLite has reached max_page_count.");

                // A transient ordinary table exercises the MAIN database rather
                // than only the TEMP database. The transaction is NEVER committed.
                await using var transaction =
                    await context.Database.BeginTransactionAsync(cancellationToken);
                // A fixed, application-owned identifier avoids dynamic SQL and
                // EF1002 entirely. The table is created and rolled back.
                await context.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE IF NOT EXISTS \"__diskchecker_write_probe\" (value INTEGER NOT NULL)",
                    cancellationToken);
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO \"__diskchecker_write_probe\" (value) VALUES (1)",
                    cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                return new(true, "SQLite transactional write/rollback successful.");
            }
            finally
            {
                if (openedByUs)
                    await context.Database.CloseConnectionAsync();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never leak the full connection string (possible SQL credentials).
            var detail = ex is SqliteException sqlite
                ? $"SQLite {sqlite.SqliteErrorCode}/{sqlite.SqliteExtendedErrorCode}: {sqlite.Message}"
                : $"{ex.GetType().Name}: database write probe failed.";
            return new(false, detail);
        }
    }

    private static async Task<long> ReadPragmaLongAsync(
        DbConnection connection, string pragma, CancellationToken cancellationToken)
    {
        // Only constant pragma names from this class are ever used.
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA " + pragma;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null ? -1 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }
}
