using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
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
                AnomaliesJson = t.AnomaliesJson
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

    private static async Task ConfigureSqliteReadOptimizationsAsync(DbConnection connection)
    {
        await using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA temp_store=MEMORY; PRAGMA cache_size=-20000;";
        await pragmaCommand.ExecuteNonQueryAsync();
    }

    private static async Task<List<SpeedSample>> LoadSpeedSeriesAsync(DbConnection connection, string tableName, int sessionId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT ProgressPercent, SpeedMBps FROM {tableName} WHERE TestSessionId = @sessionId AND SpeedMBps > 0 ORDER BY ProgressPercent, Id";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@sessionId";
        parameter.Value = sessionId;
        command.Parameters.Add(parameter);

        var values = new List<SpeedSample>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
            {
                values.Add(new SpeedSample
                {
                    ProgressPercent = reader.GetDouble(0),
                    SpeedMBps = reader.GetDouble(1)
                });
            }
        }

        return values;
    }

    private static async Task<List<SpeedSample>> LoadSpeedSeriesChunkAsync(
        DbConnection connection,
        string tableName,
        int sessionId,
        int modulo,
        int remainder)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT ProgressPercent, SpeedMBps FROM {tableName} WHERE TestSessionId = @sessionId AND SpeedMBps > 0 AND (Id % @modulo) = @remainder ORDER BY Id";

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

        var values = new List<SpeedSample>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
            {
                values.Add(new SpeedSample
                {
                    ProgressPercent = reader.GetDouble(0),
                    SpeedMBps = reader.GetDouble(1)
                });
            }
        }

        return values;
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

    public async Task<DiskCertificate> CreateCertificateAsync(DiskCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

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
        if (!string.IsNullOrWhiteSpace(incoming.ModelName) && string.IsNullOrWhiteSpace(existing.ModelName)) existing.ModelName = incoming.ModelName;
        if (!string.IsNullOrWhiteSpace(incoming.DevicePath)) existing.DevicePath = incoming.DevicePath;
        if (!string.IsNullOrWhiteSpace(incoming.DiskType) && string.IsNullOrWhiteSpace(existing.DiskType)) existing.DiskType = incoming.DiskType;
        if (!string.IsNullOrWhiteSpace(incoming.InterfaceType) && string.IsNullOrWhiteSpace(existing.InterfaceType)) existing.InterfaceType = incoming.InterfaceType;
        if (!string.IsNullOrWhiteSpace(incoming.FirmwareVersion) && string.IsNullOrWhiteSpace(existing.FirmwareVersion)) existing.FirmwareVersion = incoming.FirmwareVersion;
        if (incoming.Capacity > 0 && existing.Capacity <= 0) existing.Capacity = incoming.Capacity;
        existing.LastTestedAt = DateTime.UtcNow;
    }

    private static string GenerateCertificateNumber()
    {
        var now = DateTime.UtcNow;
        return $"DC-{now.Year}-{now.Month:D2}-{now.Day:D2}-{Guid.NewGuid().ToString("N")[..8].ToUpper(CultureInfo.InvariantCulture)}";
    }
}
