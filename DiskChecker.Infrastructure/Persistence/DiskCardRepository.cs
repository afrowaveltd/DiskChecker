using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// Entity Framework implementation of disk card repository.
/// </summary>
public class DiskCardRepository : IDiskCardRepository
{
    private readonly DiskCheckerDbContext _context;

    public DiskCardRepository(DiskCheckerDbContext context)
    {
        _context = context;
    }

    // ========== CRUD Operations ==========

    public async Task<DiskCard?> GetByIdAsync(int id)
    {
        return await _context.DiskCards
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<DiskCard?> GetBySerialNumberAsync(string serialNumber)
    {
        return await _context.DiskCards
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.SerialNumber == serialNumber);
    }

    public async Task<DiskCard?> GetByDevicePathAsync(string devicePath)
    {
        return await _context.DiskCards
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.DevicePath == devicePath);
    }

    public async Task<List<DiskCard>> GetAllAsync()
    {
        return await _context.DiskCards
            .AsNoTracking()
            .OrderByDescending(c => c.LastTestedAt)
            .ToListAsync();
    }

    public async Task<List<DiskCard>> GetActiveAsync()
    {
        return await _context.DiskCards
            .AsNoTracking()
            .Where(c => !c.IsArchived)
            .OrderByDescending(c => c.LastTestedAt)
            .ToListAsync();
    }

    public async Task<List<DiskCard>> GetArchivedAsync()
    {
        return await _context.DiskCards
            .AsNoTracking()
            .Where(c => c.IsArchived)
            .OrderByDescending(c => c.LastTestedAt)
            .ToListAsync();
    }

    public async Task<DiskCard> CreateAsync(DiskCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        // SerialNumber is unique in the DB. Some Windows/USB drives expose no reliable
        // serial; older UI flows passed an empty string, which fails on the next such
        // disk/test. Store a stable NOSN-* fallback and reuse an existing card when possible.
        card.SerialNumber = BuildSafeSerialIdentity(card);

        var existing = await _context.DiskCards
            .FirstOrDefaultAsync(c => c.SerialNumber == card.SerialNumber || c.DevicePath == card.DevicePath);
        if (existing != null)
        {
            ApplyCardMetadata(existing, card);
            await _context.SaveChangesAsync();
            return existing;
        }

        card.CreatedAt = card.CreatedAt == default ? DateTime.UtcNow : card.CreatedAt;
        card.LastTestedAt = DateTime.UtcNow;
        
        _context.DiskCards.Add(card);
        await _context.SaveChangesAsync();
        return card;
    }

    public async Task<DiskCard> UpdateAsync(DiskCard card)
    {
        _context.DiskCards.Update(card);
        await _context.SaveChangesAsync();
        return card;
    }

    public async Task DeleteAsync(int id)
    {
        var card = await _context.DiskCards.FindAsync(id);
        if (card != null)
        {
            _context.DiskCards.Remove(card);
            await _context.SaveChangesAsync();
        }
    }

    public async Task ArchiveAsync(int id, ArchiveReason reason, string? notes = null)
    {
        var card = await _context.DiskCards.FindAsync(id);
        if (card != null)
        {
            card.IsArchived = true;
            card.ArchiveReason = reason.ToString();
            card.Notes = notes;
            
            // Create archive record
            var archive = new DiskArchive
            {
                DiskCardId = id,
                ArchivedAt = DateTime.UtcNow,
                Reason = reason,
                Notes = notes,
                ArchivedBy = Environment.UserName,
                Summary = $"{card.ModelName} - {card.SerialNumber}",
                FinalGrade = card.OverallGrade,
                FinalScore = card.OverallScore,
                TotalTests = card.TestCount
            };
            
            _context.DiskArchives.Add(archive);
            await _context.SaveChangesAsync();
        }
    }

    public async Task RestoreAsync(int id)
    {
        var card = await _context.DiskCards.FindAsync(id);
        if (card != null)
        {
            card.IsArchived = false;
            card.ArchiveReason = null;
            
            // Remove archive record
            var archive = await _context.DiskArchives
                .FirstOrDefaultAsync(a => a.DiskCardId == id);
            if (archive != null)
            {
                _context.DiskArchives.Remove(archive);
            }
            
            await _context.SaveChangesAsync();
        }
    }

    public async Task<int> MergeDuplicateCardsAsync()
    {
        var cards = await _context.DiskCards
            .Include(c => c.TestSessions)
            .Include(c => c.Certificates)
            .ToListAsync();

        if (cards.Count < 2) return 0;

        static string Normalize(string? s) => (s ?? string.Empty).Trim().ToUpperInvariant();

        static string BuildIdentity(DiskCard c)
        {
            var serial = Normalize(c.SerialNumber);
            if (!string.IsNullOrWhiteSpace(serial))
                return $"SER:{serial}";

            var device = Normalize(c.DevicePath);
            var model = Normalize(c.ModelName);
            if (!string.IsNullOrWhiteSpace(device) || !string.IsNullOrWhiteSpace(model))
                return $"DEV:{device}|MOD:{model}";

            return $"ID:{c.Id}";
        }

        var mergedCount = 0;
        var groups = cards
            .GroupBy(BuildIdentity)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in groups)
        {
            var ordered = group
                .OrderByDescending(c => c.TestCount)
                .ThenByDescending(c => c.LastTestedAt)
                .ThenBy(c => c.CreatedAt)
                .ToList();

            var primary = ordered[0];
            var duplicates = ordered.Skip(1).ToList();

            foreach (var duplicate in duplicates)
            {
                var sessions = await _context.TestSessions
                    .Where(t => t.DiskCardId == duplicate.Id)
                    .ToListAsync();
                foreach (var s in sessions)
                {
                    s.DiskCardId = primary.Id;
                }

                var certs = await _context.DiskCertificates
                    .Where(c => c.DiskCardId == duplicate.Id)
                    .ToListAsync();
                foreach (var cert in certs)
                {
                    cert.DiskCardId = primary.Id;
                }

                var archives = await _context.DiskArchives
                    .Where(a => a.DiskCardId == duplicate.Id)
                    .ToListAsync();
                if (archives.Count > 0)
                {
                    _context.DiskArchives.RemoveRange(archives);
                }

                _context.DiskCards.Remove(duplicate);
                mergedCount++;
            }

            var primarySessions = await _context.TestSessions
                .Where(t => t.DiskCardId == primary.Id)
                .OrderByDescending(t => t.StartedAt)
                .ToListAsync();

            primary.TestCount = primarySessions.Count;
            if (primarySessions.Count > 0)
            {
                primary.LastTestedAt = primarySessions[0].StartedAt;
                var avg = primarySessions.Average(s => s.Score);
                primary.OverallScore = avg;
                primary.OverallGrade = avg switch
                {
                    >= 90 => "A",
                    >= 80 => "B",
                    >= 70 => "C",
                    >= 60 => "D",
                    _ => "F"
                };
            }
        }

        if (mergedCount > 0)
        {
            await _context.SaveChangesAsync();
        }

        return mergedCount;
    }

    // ========== Test Sessions ==========

    public async Task<TestSession?> GetTestSessionAsync(int sessionId)
    {
        return await _context.TestSessions
            .Include(t => t.DiskCard)
            .Include(t => t.WriteSamples)
            .Include(t => t.ReadSamples)
            .Include(t => t.TemperatureSamples)
            .Include(t => t.SmartChanges)
            .Include(t => t.Errors)
            .FirstOrDefaultAsync(t => t.Id == sessionId);
    }

    /// <summary>
    /// Načte test session bez velkých kolekcí vzorků (WriteSamples, ReadSamples, TemperatureSamples).
    /// SmartChanges a Errors se nenačítají, aby se minimalizovala zátěž SQLite.
    /// </summary>
    public async Task<TestSession?> GetTestSessionWithoutSamplesAsync(int sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        return await _context.TestSessions
            .AsNoTracking()
            .Where(t => t.Id == sessionId)
            .Select(t => new TestSession
            {
                Id = t.Id,
                DiskCardId = t.DiskCardId,
                SessionId = t.SessionId,
                TestType = t.TestType,
                StartedAt = t.StartedAt,
                CompletedAt = t.CompletedAt,
                Duration = t.Duration,
                Status = t.Status,
                IsDestructive = t.IsDestructive,
                WasLocked = t.WasLocked,
                BytesWritten = t.BytesWritten,
                AverageWriteSpeedMBps = t.AverageWriteSpeedMBps,
                MaxWriteSpeedMBps = t.MaxWriteSpeedMBps,
                MinWriteSpeedMBps = t.MinWriteSpeedMBps,
                WriteSpeedStdDev = t.WriteSpeedStdDev,
                WriteDuration = t.WriteDuration,
                WriteErrors = t.WriteErrors,
                BytesRead = t.BytesRead,
                AverageReadSpeedMBps = t.AverageReadSpeedMBps,
                MaxReadSpeedMBps = t.MaxReadSpeedMBps,
                MinReadSpeedMBps = t.MinReadSpeedMBps,
                ReadSpeedStdDev = t.ReadSpeedStdDev,
                ReadDuration = t.ReadDuration,
                ReadErrors = t.ReadErrors,
                VerificationErrors = t.VerificationErrors,
                StartTemperature = t.StartTemperature,
                MaxTemperature = t.MaxTemperature,
                AverageTemperature = t.AverageTemperature,
                PartitionCreated = t.PartitionCreated,
                PartitionScheme = t.PartitionScheme,
                WasFormatted = t.WasFormatted,
                FileSystem = t.FileSystem,
                VolumeLabel = t.VolumeLabel,
                Result = t.Result,
                Grade = t.Grade,
                Score = t.Score,
                HealthAssessment = t.HealthAssessment,
                CertificateId = t.CertificateId,
                ChartImagePath = t.ChartImagePath,
                Notes = t.Notes,
                SmartBeforeJson = t.SmartBeforeJson,
                SmartAfterJson = t.SmartAfterJson,
                SeekResultsJson = t.SeekResultsJson,
                Sanitize1ResultJson = t.Sanitize1ResultJson,
                Sanitize2ResultJson = t.Sanitize2ResultJson,
                AnomaliesJson = t.AnomaliesJson,
                // Initialize collections to prevent null reference exceptions
                Errors = new List<TestError>(),
                WriteSamples = new List<SpeedSample>(),
                ReadSamples = new List<SpeedSample>(),
                TemperatureSamples = new List<TemperatureSample>(),
                SmartChanges = new List<SmartAttributeChange>()
            })
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Načte omezený počet chyb z test session pro zobrazení v reportu.
    /// </summary>
    public async Task<List<TestError>> GetTestErrorsAsync(int sessionId, int maxErrors = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        if (maxErrors <= 0)
        {
            return new List<TestError>();
        }

        return await _context.TestSessions
            .AsNoTracking()
            .Where(t => t.Id == sessionId)
            .SelectMany(t => t.Errors)
            .OrderByDescending(e => e.IsCritical)
            .ThenByDescending(e => e.Timestamp)
            .Take(maxErrors)
            .ToListAsync();
    }

    public async Task<List<TestSession>> GetTestSessionsAsync(int diskCardId)
    {
        return await _context.TestSessions
            .AsNoTracking()
            .Where(t => t.DiskCardId == diskCardId)
            .OrderByDescending(t => t.StartedAt)
            .Select(t => new TestSession
            {
                Id = t.Id,
                DiskCardId = t.DiskCardId,
                SessionId = t.SessionId,
                TestType = t.TestType,
                StartedAt = t.StartedAt,
                CompletedAt = t.CompletedAt,
                Duration = t.Duration,
                Status = t.Status,
                IsDestructive = t.IsDestructive,
                WasLocked = t.WasLocked,
                BytesWritten = t.BytesWritten,
                AverageWriteSpeedMBps = t.AverageWriteSpeedMBps,
                MaxWriteSpeedMBps = t.MaxWriteSpeedMBps,
                MinWriteSpeedMBps = t.MinWriteSpeedMBps,
                WriteSpeedStdDev = t.WriteSpeedStdDev,
                WriteDuration = t.WriteDuration,
                WriteErrors = t.WriteErrors,
                BytesRead = t.BytesRead,
                AverageReadSpeedMBps = t.AverageReadSpeedMBps,
                MaxReadSpeedMBps = t.MaxReadSpeedMBps,
                MinReadSpeedMBps = t.MinReadSpeedMBps,
                ReadSpeedStdDev = t.ReadSpeedStdDev,
                ReadDuration = t.ReadDuration,
                ReadErrors = t.ReadErrors,
                VerificationErrors = t.VerificationErrors,
                StartTemperature = t.StartTemperature,
                MaxTemperature = t.MaxTemperature,
                AverageTemperature = t.AverageTemperature,
                PartitionCreated = t.PartitionCreated,
                PartitionScheme = t.PartitionScheme,
                WasFormatted = t.WasFormatted,
                FileSystem = t.FileSystem,
                VolumeLabel = t.VolumeLabel,
                Result = t.Result,
                Grade = t.Grade,
                Score = t.Score,
                HealthAssessment = t.HealthAssessment,
                CertificateId = t.CertificateId,
                ChartImagePath = t.ChartImagePath,
                Notes = t.Notes,
                SmartBeforeJson = t.SmartBeforeJson,
                SmartAfterJson = t.SmartAfterJson
            })
            .ToListAsync();
    }

    /// <summary>
    /// Načte uložené rychlostní vzorky pro zadanou test session bez načtení celé session.
    /// </summary>
    public async Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> GetSpeedSampleSeriesAsync(int sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await ConfigureSqliteReadOptimizationsAsync(connection);

            var writeSamples = await LoadSpeedSeriesAsync(connection, "TestSessions_WriteSamples", sessionId);
            var readSamples = await LoadSpeedSeriesAsync(connection, "TestSessions_ReadSamples", sessionId);
            return (writeSamples, readSamples);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Načte podmnožinu uložených rychlostních vzorků pro zadanou test session pomocí modulárního výběru.
    /// </summary>
    public async Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> GetSpeedSampleSeriesChunkAsync(int sessionId, int modulo, int remainder)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(modulo);

        if (remainder < 0 || remainder >= modulo)
        {
            throw new ArgumentOutOfRangeException(nameof(remainder), "Remainder musí být v rozsahu 0 až modulo-1.");
        }

        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await ConfigureSqliteReadOptimizationsAsync(connection);

            var writeSamples = await LoadSpeedSeriesChunkAsync(connection, "TestSessions_WriteSamples", sessionId, modulo, remainder);
            var readSamples = await LoadSpeedSeriesChunkAsync(connection, "TestSessions_ReadSamples", sessionId, modulo, remainder);
            return (writeSamples, readSamples);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }


    public async Task<List<TemperatureSample>> GetTemperatureSampleSeriesAsync(int sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await ConfigureSqliteReadOptimizationsAsync(connection);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Timestamp, TemperatureCelsius, Phase, ProgressPercent FROM TestSessions_TemperatureSamples WHERE TestSessionId = @sessionId ORDER BY Timestamp, Id";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@sessionId";
            parameter.Value = sessionId;
            command.Parameters.Add(parameter);

            var values = new List<TemperatureSample>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(new TemperatureSample
                {
                    Timestamp = reader.IsDBNull(0) ? DateTime.MinValue : reader.GetDateTime(0),
                    TemperatureCelsius = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    Phase = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ProgressPercent = reader.IsDBNull(3) ? 0 : reader.GetDouble(3)
                });
            }

            return values;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Načte rovnoměrně rozloženou podmnožinu rychlostních vzorků přímo z databáze.
    /// Využívá window funkci <c>ROW_NUMBER()</c> k přiřazení indexu každému
    /// vzorku seřazenému podle Id, a následně vybere pouze vzorky, jejichž index
    /// odpovídá pravidelnému kroku.  Do paměti se tak dostane maximálně
    /// <paramref name="maxPoints"/> záznamů pro zápis a stejně pro čtení.
    /// </summary>
    public async Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> GetSpeedSampleSeriesDownsampledAsync(
        int sessionId, int maxPoints, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPoints);

        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await ConfigureSqliteReadOptimizationsAsync(connection);

            var writeSamples = await LoadSpeedSeriesDownsampledAsync(
                connection, "TestSessions_WriteSamples", sessionId, maxPoints, cancellationToken);
            var readSamples = await LoadSpeedSeriesDownsampledAsync(
                connection, "TestSessions_ReadSamples", sessionId, maxPoints, cancellationToken);

            return (writeSamples, readSamples);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Načte rovnoměrně rozloženou podmnožinu teplotních vzorků s limitem
    /// <paramref name="maxPoints"/> záznamů v paměti.
    /// </summary>
    public async Task<List<TemperatureSample>> GetTemperatureSampleSeriesDownsampledAsync(
        int sessionId, int maxPoints, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPoints);

        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await ConfigureSqliteReadOptimizationsAsync(connection);

            var hasTimestampColumn = await ColumnExistsAsync(connection, "TestSessions_TemperatureSamples", "Timestamp");

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT Timestamp, TemperatureCelsius, Phase, ProgressPercent
                FROM (
                    SELECT
                        Timestamp,
                        TemperatureCelsius,
                        Phase,
                        ProgressPercent,
                        ROW_NUMBER() OVER (ORDER BY {(hasTimestampColumn ? "Timestamp, " : "")}Id) AS _rn,
                        COUNT(*) OVER () AS _total
                    FROM TestSessions_TemperatureSamples
                    WHERE TestSessionId = @sessionId
                )
                WHERE _total <= @maxPoints OR
                      ((_rn - 1) * @maxPoints / _total) <> (((_rn - 2) * @maxPoints) / _total)
                ORDER BY _rn
                LIMIT @maxPoints";

            var sessionParam = command.CreateParameter();
            sessionParam.ParameterName = "@sessionId";
            sessionParam.Value = sessionId;
            command.Parameters.Add(sessionParam);

            var maxPointsParam = command.CreateParameter();
            maxPointsParam.ParameterName = "@maxPoints";
            maxPointsParam.Value = maxPoints;
            command.Parameters.Add(maxPointsParam);

            var values = new List<TemperatureSample>(Math.Min(maxPoints, 1024));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add(new TemperatureSample
                {
                    Timestamp = reader.IsDBNull(0) ? DateTime.MinValue : reader.GetDateTime(0),
                    TemperatureCelsius = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    Phase = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ProgressPercent = reader.IsDBNull(3) ? 0 : reader.GetDouble(3)
                });
            }

            return values;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Vrátí celkový počet vzorků a počet vzorků označených jako I/O stall,
    /// aniž by se načítaly celé kolekce do paměti.
    /// </summary>
    public async Task<(int TotalSamples, int StalledSamples)> GetSpeedSampleStallInfoAsync(
        int sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await ConfigureSqliteReadOptimizationsAsync(connection);

            var writeHasStall = await ColumnExistsAsync(connection, "TestSessions_WriteSamples", "IsStalled");
            var readHasStall = await ColumnExistsAsync(connection, "TestSessions_ReadSamples", "IsStalled");
            var writeStallExpr = writeHasStall ? "SUM(CASE WHEN IsStalled <> 0 THEN 1 ELSE 0 END)" : "0";
            var readStallExpr = readHasStall ? "SUM(CASE WHEN IsStalled <> 0 THEN 1 ELSE 0 END)" : "0";

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT
                    (SELECT COUNT(*) FROM TestSessions_WriteSamples WHERE TestSessionId = @sessionId) +
                    (SELECT COUNT(*) FROM TestSessions_ReadSamples WHERE TestSessionId = @sessionId),
                    (SELECT {writeStallExpr} FROM TestSessions_WriteSamples WHERE TestSessionId = @sessionId) +
                    (SELECT {readStallExpr} FROM TestSessions_ReadSamples WHERE TestSessionId = @sessionId)";

            var param = command.CreateParameter();
            param.ParameterName = "@sessionId";
            param.Value = sessionId;
            command.Parameters.Add(param);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var total = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                var stalled = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                return (total, stalled);
            }

            return (0, 0);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Načte rovnoměrně rozloženou podmnožinu vzorků z jedné tabulky pomocí
    /// window funkce <c>ROW_NUMBER()</c>.  Funguje na SQLite >= 3.25.
    /// </summary>
    private static async Task<List<SpeedSample>> LoadSpeedSeriesDownsampledAsync(
        DbConnection connection,
        string tableName,
        int sessionId,
        int maxPoints,
        CancellationToken cancellationToken)
    {
        var hasIsStalledColumn = await ColumnExistsAsync(connection, tableName, "IsStalled");
        var hasTimestampColumn = await ColumnExistsAsync(connection, tableName, "Timestamp");
        var hasBytesProcessedColumn = await ColumnExistsAsync(connection, tableName, "BytesProcessed");
        var timestampSelect = hasTimestampColumn ? "Timestamp" : "NULL AS Timestamp";
        var bytesSelect = hasBytesProcessedColumn ? "BytesProcessed" : "0 AS BytesProcessed";
        var stalledSelect = hasIsStalledColumn ? "IsStalled" : "0 AS IsStalled";
        var orderClause = hasTimestampColumn ? "Timestamp, Id" : "Id";

        await using var command = connection.CreateCommand();
        command.CommandText = $@"
            SELECT ProgressPercent, SpeedMBps, {timestampSelect}, {bytesSelect}, {stalledSelect}
            FROM (
                SELECT
                    ProgressPercent,
                    SpeedMBps,
                    {timestampSelect},
                    {bytesSelect},
                    {stalledSelect},
                    ROW_NUMBER() OVER (ORDER BY {orderClause}) AS _rn,
                    COUNT(*) OVER () AS _total
                FROM {tableName}
                WHERE TestSessionId = @sessionId
                  AND (SpeedMBps > 0 OR {stalledSelect} <> 0)
            )
            WHERE _total <= @maxPoints OR
                  ((_rn - 1) * @maxPoints / _total) <> (((_rn - 2) * @maxPoints) / _total)
            ORDER BY _rn
            LIMIT @maxPoints";

        var sessionParam = command.CreateParameter();
        sessionParam.ParameterName = "@sessionId";
        sessionParam.Value = sessionId;
        command.Parameters.Add(sessionParam);

        var maxPointsParam = command.CreateParameter();
        maxPointsParam.ParameterName = "@maxPoints";
        maxPointsParam.Value = maxPoints;
        command.Parameters.Add(maxPointsParam);

        return await ReadSpeedSamplesAsync(command);
    }

    private static async Task ConfigureSqliteReadOptimizationsAsync(DbConnection connection)
    {
        await using var pragmaCommand = connection.CreateCommand();
        // IMPORTANT: Do NOT use temp_store=MEMORY here. Window-function downsampling
        // (ROW_NUMBER + COUNT OVER) forces SQLite to sort millions of rows. With
        // temp_store=MEMORY the entire sort spills into the application's virtual
        // memory, which can exhaust all system RAM on large sanitization tests
        // (500GB+ drives) and crash the OS. Default temp_store=FILE allows SQLite
        // to use temporary disk files for large intermediate results.
        pragmaCommand.CommandText = "PRAGMA cache_size=-20000;";
        await pragmaCommand.ExecuteNonQueryAsync();
    }

    private static async Task<List<SpeedSample>> LoadSpeedSeriesAsync(DbConnection connection, string tableName, int sessionId)
    {
        var hasIsStalledColumn = await ColumnExistsAsync(connection, tableName, "IsStalled");
        var hasTimestampColumn = await ColumnExistsAsync(connection, tableName, "Timestamp");
        var hasBytesProcessedColumn = await ColumnExistsAsync(connection, tableName, "BytesProcessed");
        var timestampSelect = hasTimestampColumn ? "Timestamp" : "NULL AS Timestamp";
        var bytesSelect = hasBytesProcessedColumn ? "BytesProcessed" : "0 AS BytesProcessed";

        await using var command = connection.CreateCommand();
        command.CommandText = hasIsStalledColumn
            ? $"SELECT ProgressPercent, SpeedMBps, {timestampSelect}, {bytesSelect}, IsStalled FROM {tableName} WHERE TestSessionId = @sessionId AND (SpeedMBps > 0 OR IsStalled <> 0) ORDER BY {(hasTimestampColumn ? "Timestamp," : string.Empty)} ProgressPercent, Id"
            : $"SELECT ProgressPercent, SpeedMBps, {timestampSelect}, {bytesSelect}, 0 AS IsStalled FROM {tableName} WHERE TestSessionId = @sessionId AND SpeedMBps > 0 ORDER BY {(hasTimestampColumn ? "Timestamp," : string.Empty)} ProgressPercent, Id";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@sessionId";
        parameter.Value = sessionId;
        command.Parameters.Add(parameter);

        return await ReadSpeedSamplesAsync(command);
    }

    private static async Task<List<SpeedSample>> LoadSpeedSeriesChunkAsync(
        DbConnection connection,
        string tableName,
        int sessionId,
        int modulo,
        int remainder)
    {
        var hasIsStalledColumn = await ColumnExistsAsync(connection, tableName, "IsStalled");
        var hasTimestampColumn = await ColumnExistsAsync(connection, tableName, "Timestamp");
        var hasBytesProcessedColumn = await ColumnExistsAsync(connection, tableName, "BytesProcessed");
        var timestampSelect = hasTimestampColumn ? "Timestamp" : "NULL AS Timestamp";
        var bytesSelect = hasBytesProcessedColumn ? "BytesProcessed" : "0 AS BytesProcessed";

        await using var command = connection.CreateCommand();
        command.CommandText = hasIsStalledColumn
            ? $"SELECT ProgressPercent, SpeedMBps, {timestampSelect}, {bytesSelect}, IsStalled FROM {tableName} WHERE TestSessionId = @sessionId AND (SpeedMBps > 0 OR IsStalled <> 0) AND (Id % @modulo) = @remainder ORDER BY Id"
            : $"SELECT ProgressPercent, SpeedMBps, {timestampSelect}, {bytesSelect}, 0 AS IsStalled FROM {tableName} WHERE TestSessionId = @sessionId AND SpeedMBps > 0 AND (Id % @modulo) = @remainder ORDER BY Id";

        var sessionParameter = command.CreateParameter();
        sessionParameter.ParameterName = "@sessionId";
        sessionParameter.Value = sessionId;
        command.Parameters.Add(sessionParameter);

        var moduloParameter = command.CreateParameter();
        moduloParameter.ParameterName = "@modulo";
        moduloParameter.Value = modulo;
        command.Parameters.Add(moduloParameter);

        var remainderParameter = command.CreateParameter();
        remainderParameter.ParameterName = "@remainder";
        remainderParameter.Value = remainder;
        command.Parameters.Add(remainderParameter);

        return await ReadSpeedSamplesAsync(command);
    }

    private static async Task<List<SpeedSample>> ReadSpeedSamplesAsync(DbCommand command)
    {
        var values = new List<SpeedSample>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
            {
                values.Add(new SpeedSample
                {
                    ProgressPercent = reader.GetDouble(0),
                    SpeedMBps = reader.GetDouble(1),
                    Timestamp = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2),
                    BytesProcessed = reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                    IsStalled = !reader.IsDBNull(4) && reader.GetBoolean(4)
                });
            }
        }

        return values;
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection connection, string tableName, string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(1) && string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }



    public async Task CreateTelemetrySamplesAsync(int sessionId, TelemetrySamplePhase phase, IReadOnlyCollection<SpeedSample> samples, bool replacePhase = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentNullException.ThrowIfNull(samples);

        if (replacePhase)
        {
            var existing = await _context.TestTelemetrySamples
                .Where(s => s.TestSessionId == sessionId && s.Phase == phase)
                .ToListAsync();
            if (existing.Count > 0)
            {
                _context.TestTelemetrySamples.RemoveRange(existing);
            }
        }

        var ordered = SpeedSampleRetentionService
            .ReduceForPersistenceWithReasons(samples, TelemetryRetentionProfile.Balanced)
            .ToList();

        if (ordered.Count == 0)
        {
            if (replacePhase)
                await _context.SaveChangesAsync();
            return;
        }

        var firstTimestamp = ordered.FirstOrDefault(s => s.Sample.Timestamp != default)?.Sample.Timestamp;
        var records = ordered.Select((retained, index) =>
        {
            var sample = retained.Sample;
            return new TestTelemetrySample
            {
                TestSessionId = sessionId,
                Phase = phase,
                SequenceIndex = index + 1,
                TimestampUtc = sample.Timestamp == default ? DateTime.UtcNow : sample.Timestamp,
                ElapsedMs = sample.Elapsed?.TotalMilliseconds ?? (firstTimestamp.HasValue && sample.Timestamp != default
                    ? (sample.Timestamp - firstTimestamp.Value).TotalMilliseconds
                    : null),
                ProgressPercent = sample.ProgressPercent,
                BytesProcessed = sample.BytesProcessed,
                SpeedMBps = sample.SpeedMBps,
                IsStalled = sample.IsStalled,
                IsAnomaly = retained.RetentionReason.Contains("Anomaly", StringComparison.OrdinalIgnoreCase),
                RetentionReason = retained.RetentionReason
            };
        }).ToList();

        _context.TestTelemetrySamples.AddRange(records);

        var existingStalls = await _context.TestStallEvents
            .Where(e => e.TestSessionId == sessionId && e.Phase == phase)
            .ToListAsync();
        if (existingStalls.Count > 0)
        {
            _context.TestStallEvents.RemoveRange(existingStalls);
        }

        var stallEvents = BuildStallEvents(sessionId, phase, ordered);
        if (stallEvents.Count > 0)
        {
            _context.TestStallEvents.AddRange(stallEvents);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<List<TestTelemetrySample>> GetTelemetrySamplesAsync(int sessionId, TelemetrySamplePhase? phase = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        var query = _context.TestTelemetrySamples
            .AsNoTracking()
            .Where(s => s.TestSessionId == sessionId);

        if (phase.HasValue)
            query = query.Where(s => s.Phase == phase.Value);

        return await query
            .OrderBy(s => s.Phase)
            .ThenBy(s => s.SequenceIndex)
            .ToListAsync();
    }



    private static List<TestStallEvent> BuildStallEvents(int sessionId, TelemetrySamplePhase phase, IReadOnlyList<RetainedSpeedSample> ordered)
    {
        var events = new List<TestStallEvent>();
        var i = 0;
        while (i < ordered.Count)
        {
            if (!ordered[i].Sample.IsStalled)
            {
                i++;
                continue;
            }

            var start = i;
            while (i + 1 < ordered.Count && ordered[i + 1].Sample.IsStalled)
            {
                i++;
            }
            var end = i;

            var startSample = ordered[start].Sample;
            var endSample = ordered[end].Sample;
            var startedAt = startSample.Timestamp == default ? DateTime.UtcNow : startSample.Timestamp;
            var endedAt = endSample.Timestamp == default ? startedAt : endSample.Timestamp;
            if (endedAt < startedAt)
            {
                endedAt = startedAt;
            }

            var before = start > 0 ? ordered[start - 1].Sample.SpeedMBps : (double?)null;
            var after = end + 1 < ordered.Count ? ordered[end + 1].Sample.SpeedMBps : (double?)null;
            events.Add(new TestStallEvent
            {
                TestSessionId = sessionId,
                Phase = phase,
                StartedAtUtc = startedAt,
                EndedAtUtc = endedAt,
                DurationMs = Math.Max(0, (endedAt - startedAt).TotalMilliseconds),
                StartProgressPercent = startSample.ProgressPercent,
                EndProgressPercent = endSample.ProgressPercent,
                BytesProcessed = endSample.BytesProcessed != 0 ? endSample.BytesProcessed : startSample.BytesProcessed,
                LastSpeedBeforeStallMBps = before > 0 ? before : null,
                FirstSpeedAfterStallMBps = after > 0 ? after : null
            });

            i++;
        }

        return events;
    }


    public async Task CreateStallEventsAsync(int sessionId, TelemetrySamplePhase phase, IReadOnlyCollection<TestStallEvent> events, bool replacePhase = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentNullException.ThrowIfNull(events);

        if (replacePhase)
        {
            var existing = await _context.TestStallEvents
                .Where(e => e.TestSessionId == sessionId && e.Phase == phase)
                .ToListAsync();
            if (existing.Count > 0)
            {
                _context.TestStallEvents.RemoveRange(existing);
            }
        }

        if (events.Count > 0)
        {
            var records = events.Select(e => new TestStallEvent
            {
                TestSessionId = sessionId,
                Phase = phase,
                StartedAtUtc = e.StartedAtUtc,
                EndedAtUtc = e.EndedAtUtc,
                DurationMs = e.DurationMs,
                StartProgressPercent = e.StartProgressPercent,
                EndProgressPercent = e.EndProgressPercent,
                BytesProcessed = e.BytesProcessed,
                LastSpeedBeforeStallMBps = e.LastSpeedBeforeStallMBps,
                FirstSpeedAfterStallMBps = e.FirstSpeedAfterStallMBps
            }).ToList();
            _context.TestStallEvents.AddRange(records);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<List<TestStallEvent>> GetStallEventsAsync(int sessionId, TelemetrySamplePhase? phase = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        var query = _context.TestStallEvents
            .AsNoTracking()
            .Where(e => e.TestSessionId == sessionId);
        if (phase.HasValue)
            query = query.Where(e => e.Phase == phase.Value);

        return await query
            .OrderBy(e => e.Phase)
            .ThenBy(e => e.StartedAtUtc)
            .ToListAsync();
    }

    public async Task CreateAnomalyEventsAsync(int sessionId, IReadOnlyCollection<SpeedAnomaly> anomalies, bool replaceExisting = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentNullException.ThrowIfNull(anomalies);

        if (replaceExisting)
        {
            var existing = await _context.TestAnomalyEvents
                .Where(a => a.TestSessionId == sessionId)
                .ToListAsync();
            if (existing.Count > 0)
            {
                _context.TestAnomalyEvents.RemoveRange(existing);
            }
        }

        var events = anomalies.Select(a => new TestAnomalyEvent
        {
            TestSessionId = sessionId,
            Phase = MapTelemetryPhase(a.Phase),
            StartStandardIndex = a.StartStandardIndex,
            EndStandardIndex = a.EndStandardIndex,
            StartProgressPercent = a.StartProgressPercent,
            EndProgressPercent = a.EndProgressPercent,
            StartBytesProcessed = a.StartBytesProcessed,
            EndBytesProcessed = a.EndBytesProcessed,
            StartLba512 = a.StartLba512,
            EndLba512 = a.EndLba512,
            DurationMs = a.DurationMs,
            MinSpeedMBps = a.MinSpeedMBps,
            MaxSpeedMBps = a.MaxSpeedMBps,
            AvgSpeedMBps = a.AvgSpeedMBps,
            EntrySpeedMBps = a.EntrySpeedMBps,
            ExitSpeedMBps = a.ExitSpeedMBps,
            MaxDeviationPercent = a.MaxDeviationPercent,
            SeverityScore = a.SeverityScore,
            OverlayGroup = a.OverlayGroup,
            DefectType = a.DefectType
        }).ToList();

        if (events.Count > 0)
        {
            _context.TestAnomalyEvents.AddRange(events);
        }

        foreach (var group in anomalies.GroupBy(a => MapTelemetryPhase(a.Phase)))
        {
            var highResSamples = group
                .SelectMany(a => a.HighResSamples.Select(s => new SpeedSample
                {
                    Timestamp = s.Timestamp,
                    ProgressPercent = s.ProgressPercent,
                    BytesProcessed = s.BytesProcessed,
                    SpeedMBps = s.SpeedMBps,
                    IsStalled = s.IsStalled
                }))
                .Where(s => s.SpeedMBps > 0 || s.IsStalled)
                .OrderBy(s => s.Timestamp == default ? DateTime.MaxValue : s.Timestamp)
                .ThenBy(s => s.ProgressPercent)
                .ToList();

            if (highResSamples.Count == 0)
            {
                continue;
            }

            var existingCount = await _context.TestTelemetrySamples
                .CountAsync(t => t.TestSessionId == sessionId && t.Phase == group.Key);
            var firstTimestamp = highResSamples.FirstOrDefault(s => s.Timestamp != default)?.Timestamp;
            var records = highResSamples.Select((sample, index) => new TestTelemetrySample
            {
                TestSessionId = sessionId,
                Phase = group.Key,
                SequenceIndex = existingCount + index + 1,
                TimestampUtc = sample.Timestamp == default ? DateTime.UtcNow : sample.Timestamp,
                ElapsedMs = firstTimestamp.HasValue && sample.Timestamp != default
                    ? (sample.Timestamp - firstTimestamp.Value).TotalMilliseconds
                    : null,
                ProgressPercent = sample.ProgressPercent,
                BytesProcessed = sample.BytesProcessed,
                SpeedMBps = sample.SpeedMBps,
                IsStalled = sample.IsStalled,
                IsAnomaly = true,
                RetentionReason = sample.IsStalled ? "Anomaly+Stall" : "Anomaly"
            }).ToList();
            _context.TestTelemetrySamples.AddRange(records);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<List<TestAnomalyEvent>> GetAnomalyEventsAsync(int sessionId, TelemetrySamplePhase? phase = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        var query = _context.TestAnomalyEvents
            .AsNoTracking()
            .Where(a => a.TestSessionId == sessionId);
        if (phase.HasValue)
            query = query.Where(a => a.Phase == phase.Value);

        return await query
            .OrderByDescending(a => a.SeverityScore)
            .ThenBy(a => a.StartProgressPercent)
            .ToListAsync();
    }

    private static TelemetrySamplePhase MapTelemetryPhase(string? phase)
    {
        return string.Equals(phase, "Read", StringComparison.OrdinalIgnoreCase)
            ? TelemetrySamplePhase.Read
            : TelemetrySamplePhase.Write;
    }

    public async Task CreateSeekSamplesAsync(int sessionId, SeekTestType testType, IReadOnlyCollection<SeekLatencySample> samples)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
        {
            return;
        }

        var existing = await _context.SeekSamples
            .Where(s => s.TestSessionId == sessionId && s.TestType == testType)
            .ToListAsync();
        if (existing.Count > 0)
        {
            _context.SeekSamples.RemoveRange(existing);
        }

        var records = samples
            .Where(s => s.Index > 0)
            .OrderBy(s => s.Index)
            .Select(s => new SeekSampleRecord
            {
                TestSessionId = sessionId,
                TestType = testType,
                Index = s.Index,
                SourceLba = s.SourceLba,
                DestinationLba = s.DestinationLba,
                SeekDistance = s.SeekDistance,
                LatencyMs = s.LatencyMs,
                TimestampUtc = s.TimestampUtc == default ? DateTime.UtcNow : s.TimestampUtc,
                HasError = s.HasError,
                ErrorMessage = s.ErrorMessage
            })
            .ToList();

        if (records.Count == 0)
        {
            await _context.SaveChangesAsync();
            return;
        }

        _context.SeekSamples.AddRange(records);
        await _context.SaveChangesAsync();
    }

    public async Task<List<SeekSampleRecord>> GetSeekSamplesAsync(int sessionId, SeekTestType? testType = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        var query = _context.SeekSamples
            .AsNoTracking()
            .Where(s => s.TestSessionId == sessionId);

        if (testType.HasValue)
        {
            query = query.Where(s => s.TestType == testType.Value);
        }

        return await query
            .OrderBy(s => s.TestType)
            .ThenBy(s => s.Index)
            .ToListAsync();
    }

    public async Task<TestSession> CreateTestSessionAsync(TestSession session)
    {
        session.SessionId = Guid.NewGuid();
        session.StartedAt = DateTime.UtcNow;

        _context.TestSessions.Add(session);

        var card = await _context.DiskCards.FindAsync(session.DiskCardId);
        if (card != null)
        {
            card.TestCount++;
            card.LastTestedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        // Also persist analysis-oriented telemetry in a dedicated table. The legacy
        // owned collections remain for current screens/certificates, but this table
        // is the future source for zoomable historical analysis.
        if (session.WriteSamples.Count > 0)
        {
            await CreateTelemetrySamplesAsync(session.Id, TelemetrySamplePhase.Write, session.WriteSamples);
        }

        if (session.ReadSamples.Count > 0)
        {
            await CreateTelemetrySamplesAsync(session.Id, TelemetrySamplePhase.Read, session.ReadSamples);
        }

        if (session.Anomalies.Count > 0)
        {
            await CreateAnomalyEventsAsync(session.Id, session.Anomalies);
        }

        return session;
    }

    public async Task<TestSession> UpdateTestSessionAsync(TestSession session)
    {
        _context.TestSessions.Update(session);
        await _context.SaveChangesAsync();
        return session;
    }

    public async Task DeleteTestSessionAsync(int sessionId)
    {
        var session = await _context.TestSessions.FindAsync(sessionId);
        if (session != null)
        {
            _context.TestSessions.Remove(session);
            await _context.SaveChangesAsync();
        }
    }

    // ========== Certificates ==========

    public async Task<DiskCertificate?> GetCertificateAsync(int certificateId)
    {
        return await _context.DiskCertificates
            .Include(c => c.DiskCard)
            .Include(c => c.TestSession)
            .FirstOrDefaultAsync(c => c.Id == certificateId);
    }

    public async Task<DiskCertificate?> GetLatestCertificateAsync(int diskCardId)
    {
        return await _context.DiskCertificates
            .Where(c => c.DiskCardId == diskCardId)
            .OrderByDescending(c => c.GeneratedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<DiskCertificate>> GetCertificatesAsync(int diskCardId)
    {
        return await _context.DiskCertificates
            .Where(c => c.DiskCardId == diskCardId)
            .OrderByDescending(c => c.GeneratedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Vyčistí change tracker DbContextu.  Používá se před přidáním nového
    /// certifikátu k předejití konfliktů sledování vlastněných entit.
    /// </summary>
    public void ClearChangeTracker()
    {
        _context.ChangeTracker.Clear();
    }

    public async Task<DiskCertificate> CreateCertificateAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        // Clear change tracker to prevent conflicts with owned entities
        // (SmartAttributeSummary) from previously loaded certificates.
        // Certificate generation creates a fresh DiskCertificate with new
        // SmartAttributeSummary owned entities (Id = 0). If the DbContext is
        // already tracking another certificate (e.g. from GetLatestCertificateAsync),
        // its SmartAttributes may have the same primary key values (auto-generated),
        // causing "another instance with the same key value" tracking errors.
        _context.ChangeTracker.Clear();

        if (certificate.Id > 0)
        {
            await UpdateCertificateAsync(certificate);
            return certificate;
        }

        if (certificate.DiskCardId <= 0 || !await _context.DiskCards.AnyAsync(c => c.Id == certificate.DiskCardId))
        {
            throw new InvalidOperationException($"Certificate cannot be saved because DiskCardId {certificate.DiskCardId} does not exist.");
        }

        if (certificate.TestSessionId <= 0 || !await _context.TestSessions.AnyAsync(s => s.Id == certificate.TestSessionId))
        {
            throw new InvalidOperationException($"Certificate cannot be saved because TestSessionId {certificate.TestSessionId} does not exist.");
        }

        if (string.IsNullOrWhiteSpace(certificate.CertificateNumber))
        {
            certificate.CertificateNumber = GenerateCertificateNumber();
        }
        else if (await _context.DiskCertificates.AnyAsync(c => c.CertificateNumber == certificate.CertificateNumber))
        {
            certificate.CertificateNumber = GenerateCertificateNumber();
        }

        if (certificate.GeneratedAt == default)
        {
            certificate.GeneratedAt = DateTime.UtcNow;
        }
        
        _context.DiskCertificates.Add(certificate);
        await _context.SaveChangesAsync();

        var session = await _context.TestSessions.FindAsync(certificate.TestSessionId);
        if (session != null && session.CertificateId != certificate.Id)
        {
            session.CertificateId = certificate.Id;
            await _context.SaveChangesAsync();
        }

        return certificate;
    }

    public async Task UpdateCertificateAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (certificate.Id <= 0)
        {
            await CreateCertificateAsync(certificate);
            return;
        }

        _context.DiskCertificates.Update(certificate);
        await _context.SaveChangesAsync();

        var session = await _context.TestSessions.FindAsync(certificate.TestSessionId);
        if (session != null && session.CertificateId != certificate.Id)
        {
            session.CertificateId = certificate.Id;
            await _context.SaveChangesAsync();
        }
    }

    // ========== Comparisons ==========

    public async Task<List<DiskCard>> GetBestDisksAsync(int count = 10)
    {
        return await _context.DiskCards
            .Where(c => !c.IsArchived)
            .OrderByDescending(c => c.OverallScore)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<DiskCard>> GetByGradeAsync(string grade)
    {
        return await _context.DiskCards
            .Where(c => c.OverallGrade == grade && !c.IsArchived)
            .OrderByDescending(c => c.OverallScore)
            .ToListAsync();
    }

    public async Task<Dictionary<string, List<DiskCard>>> GetByHealthStatusAsync()
    {
        var disks = await _context.DiskCards
            .Where(c => !c.IsArchived)
            .ToListAsync();
        
        return disks
            .GroupBy(c => c.OverallGrade)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static string BuildSafeSerialIdentity(DiskCard card)
    {
        var serial = NormalizeIdentityToken(card.SerialNumber);
        if (IsReliableSerialIdentity(serial))
        {
            return serial.Length <= 120 ? serial : serial[..120];
        }

        var fingerprint = string.Join("|",
            card.DevicePath?.Trim() ?? string.Empty,
            card.ModelName?.Trim() ?? string.Empty,
            card.FirmwareVersion?.Trim() ?? string.Empty,
            card.Capacity.ToString(CultureInfo.InvariantCulture));

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))[..24];
        return $"NOSN-{hash}";
    }

    private static string NormalizeIdentityToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (!char.IsWhiteSpace(ch) && ch != '-' && ch != '_')
            {
                sb.Append(char.ToUpperInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private static bool IsReliableSerialIdentity(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.Length < 4)
        {
            return false;
        }

        if (serial.StartsWith("NOSN", StringComparison.OrdinalIgnoreCase) || serial.All(ch => ch == serial[0]))
        {
            return false;
        }

        return serial is not "UNKNOWN" and not "N/A" and not "NONE" and not "NOTAVAILABLE" and not "SERIALNUMBER" and not "GENERIC" and not "DEFAULT";
    }

    private static void ApplyCardMetadata(DiskCard existing, DiskCard incoming)
    {
        // Always update device path and serial number (disk may have been swapped on same port)
        if (!string.IsNullOrWhiteSpace(incoming.DevicePath)) existing.DevicePath = incoming.DevicePath;
        if (!string.IsNullOrWhiteSpace(incoming.SerialNumber)) existing.SerialNumber = incoming.SerialNumber;
        // Update model/type/firmware/capacity — new disk on same path should overwrite old metadata
        if (!string.IsNullOrWhiteSpace(incoming.ModelName)) existing.ModelName = incoming.ModelName;
        if (!string.IsNullOrWhiteSpace(incoming.DiskType)) existing.DiskType = incoming.DiskType;
        if (!string.IsNullOrWhiteSpace(incoming.InterfaceType)) existing.InterfaceType = incoming.InterfaceType;
        if (!string.IsNullOrWhiteSpace(incoming.FirmwareVersion)) existing.FirmwareVersion = incoming.FirmwareVersion;
        if (incoming.Capacity > 0) existing.Capacity = incoming.Capacity;
        existing.LastTestedAt = DateTime.UtcNow;
    }

    // ========== SMART Snapshots ==========

    public async Task<SmartSnapshotRecord> CreateSmartSnapshotAsync(SmartSnapshotRecord snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.DiskCardId <= 0)
            throw new InvalidOperationException("DiskCardId must be set.");

        if (snapshot.RetrievedAtUtc == default)
            snapshot.RetrievedAtUtc = DateTime.UtcNow;

        _context.SmartSnapshots.Add(snapshot);
        await _context.SaveChangesAsync();
        return snapshot;
    }

    public async Task<List<SmartSnapshotRecord>> GetSmartSnapshotsAsync(int diskCardId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diskCardId);

        return await _context.SmartSnapshots
            .AsNoTracking()
            .Where(s => s.DiskCardId == diskCardId)
            .OrderBy(s => s.RetrievedAtUtc)
            .ToListAsync();
    }

    public async Task<List<SmartSnapshotRecord>> GetSmartSnapshotsInRangeAsync(int diskCardId, DateTime fromUtc, DateTime toUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diskCardId);

        return await _context.SmartSnapshots
            .AsNoTracking()
            .Where(s => s.DiskCardId == diskCardId && s.RetrievedAtUtc >= fromUtc && s.RetrievedAtUtc <= toUtc)
            .OrderBy(s => s.RetrievedAtUtc)
            .ToListAsync();
    }

    public async Task DeleteSmartSnapshotsAsync(int diskCardId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(diskCardId);

        var snapshots = await _context.SmartSnapshots
            .Where(s => s.DiskCardId == diskCardId)
            .ToListAsync();

        if (snapshots.Count > 0)
        {
            _context.SmartSnapshots.RemoveRange(snapshots);
            await _context.SaveChangesAsync();
        }
    }

    private static string GenerateCertificateNumber()
    {
        var now = DateTime.UtcNow;
        return $"DC-{now.Year}-{now.Month:D2}-{now.Day:D2}-{Guid.NewGuid().ToString("N")[..8].ToUpper(CultureInfo.InvariantCulture)}";
    }
}
