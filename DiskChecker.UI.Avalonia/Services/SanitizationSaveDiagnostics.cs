using System;
using System.Globalization;
using System.IO;
using DiskChecker.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Explains why a sanitization result or its optional certificate could not
/// be written. Does not modify the database, delete WAL files or retry writes.
/// </summary>
public static class SanitizationSaveDiagnostics
{
    /// <summary>
    /// The session is durable as soon as SaveSanitizationAsync returns;
    /// failure of a subsequent certificate/PDF is a different error.
    /// </summary>
    public static string FailureStatusKey(bool sessionPersisted) =>
        sessionPersisted
            ? "SurfaceTest.Status.SanitizeArtifactsFailed"
            : "SurfaceTest.Status.SanitizeSaveFailed";

    /// <summary>
    /// Return the underlying error, plus safe SQLite file-location diagnostics
    /// when an SQLITE_FULL (13) error occurs. Never reveal a connection password.
    /// </summary>
    public static string Describe(Exception exception, DatabaseStorageSettings? storage)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var sqliteError = FindSqliteException(exception);
        var message = sqliteError?.Message
            ?? (exception is DbUpdateException db
                ? db.InnerException?.Message ?? db.Message
                : exception.Message);

        if (sqliteError?.SqliteErrorCode != 13 ||
            storage?.Provider != DatabaseProviderKind.Sqlite)
            return message;

        var output = message + Environment.NewLine +
            $"SQLite error 13 (extended code {sqliteError.SqliteExtendedErrorCode}). " +
            "The problem concerns database writes, not necessarily the tested HDD.";

        try
        {
            var connection = new SqliteConnectionStringBuilder(
                string.IsNullOrWhiteSpace(storage.ConnectionString)
                    ? DatabaseStorageSettings.Default.ConnectionString!
                    : storage.ConnectionString);
            var source = connection.DataSource;
            if (string.IsNullOrWhiteSpace(source) ||
                string.Equals(source, ":memory:", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return output;

            var databasePath = Path.GetFullPath(source);
            output += Environment.NewLine + "SQLite database: " + databasePath;

            var root = Path.GetPathRoot(databasePath);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var disk = new DriveInfo(root);
                if (disk.IsReady)
                {
                    output += Environment.NewLine +
                        "Database volume available: " +
                        (disk.AvailableFreeSpace / (1024d * 1024 * 1024))
                            .ToString("F2", CultureInfo.InvariantCulture) + " GiB";
                }
            }
        }
        catch (Exception error) when (error is ArgumentException or
            IOException or UnauthorizedAccessException or
            NotSupportedException or InvalidOperationException)
        {
            // Diagnostics must never mask the original failed save.
        }

        output += Environment.NewLine + "TEMP folder: " + Path.GetTempPath() +
            Environment.NewLine +
            "Check SQLite WAL/quota/max_page_count and TEMP volume; do not " +
            "delete a live -wal/-shm file or run VACUUM during a failed session.";

        return output;
    }

    private static SqliteException? FindSqliteException(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is SqliteException sqlite)
                return sqlite;
        }

        return null;
    }
}
