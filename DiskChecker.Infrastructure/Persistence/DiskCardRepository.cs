using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            .Where(c => c.IsArchived)
            .OrderByDescending(c => c.LastTestedAt)
            .ToListAsync();
    }

    public async Task<DiskCard> CreateAsync(DiskCard card)
    {
        card.CreatedAt = DateTime.UtcNow;
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
                Notes = t.Notes
            })
            .ToListAsync();
    }

    public async Task<TestSession> CreateTestSessionAsync(TestSession session)
    {
        session.SessionId = Guid.NewGuid();
        session.StartedAt = DateTime.UtcNow;
        
        _context.TestSessions.Add(session);
        
        // Update disk card
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
        certificate.CertificateNumber = GenerateCertificateNumber();
        certificate.GeneratedAt = DateTime.UtcNow;
        
        _context.DiskCertificates.Add(certificate);
        await _context.SaveChangesAsync();
        return certificate;
    }

    public async Task UpdateCertificateAsync(DiskCertificate certificate)
    {
        _context.DiskCertificates.Update(certificate);
        await _context.SaveChangesAsync();
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

    private static string GenerateCertificateNumber()
    {
        var now = DateTime.UtcNow;
        return $"DC-{now.Year}-{now.Month:D2}-{now.Day:D2}-{Guid.NewGuid().ToString("N")[..8].ToUpper(CultureInfo.InvariantCulture)}";
    }
}