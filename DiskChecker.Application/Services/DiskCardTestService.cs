using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Application.Services;

/// <summary>
/// Service for saving test results to disk cards.
/// Ensures all tests (SMART, surface, sanitization) are properly recorded.
/// </summary>
public class DiskCardTestService
{
    private readonly DiskCheckerDbContext _dbContext;
    private readonly ILogger<DiskCardTestService>? _logger;

    private static readonly Action<ILogger, string, string, Exception?> LogCardCreated =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, nameof(DiskCardTestService)),
            "Created new disk card for {Model} (S/N: {Serial})");

    private static readonly Action<ILogger, int, string, Exception?> LogSmartSaved =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(2, nameof(DiskCardTestService)),
            "Saved SMART check for disk card {CardId}: Grade {Grade}");

    private static readonly Action<ILogger, int, string, int, Exception?> LogSurfaceSaved =
        LoggerMessage.Define<int, string, int>(
            LogLevel.Information,
            new EventId(3, nameof(DiskCardTestService)),
            "Saved surface test for disk card {CardId}: Grade {Grade}, Errors {Errors}");

    private static readonly Action<ILogger, int, bool, int, Exception?> LogSanitizationSaved =
        LoggerMessage.Define<int, bool, int>(
            LogLevel.Information,
            new EventId(4, nameof(DiskCardTestService)),
            "Saved sanitization for disk card {CardId}: Success {Success}, Errors {Errors}");

    public DiskCardTestService(DiskCheckerDbContext dbContext, ILogger<DiskCardTestService>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Get or create a disk card for a drive.
    /// </summary>
    public async Task<DiskCard> GetOrCreateCardAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);

        var serialKey = BuildIdentityKey(drive);

        var card = await _dbContext.DiskCards
            .FirstOrDefaultAsync(c => c.SerialNumber == serialKey, cancellationToken);

        if (card == null)
        {
            card = new DiskCard
            {
                ModelName = drive.Name ?? "Unknown",
                SerialNumber = serialKey,
                DevicePath = drive.Path,
                DiskType = DetermineDiskType(drive),
                InterfaceType = DetermineInterfaceType(drive),
                Capacity = drive.TotalSize,
                FirmwareVersion = string.Empty,
                ConnectionType = drive.IsRemovable ? "External" : "Internal",
                CreatedAt = DateTime.UtcNow,
                LastTestedAt = DateTime.UtcNow,
                OverallGrade = "?",
                OverallScore = 0,
                TestCount = 0,
                IsArchived = false
            };

            _dbContext.DiskCards.Add(card);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (_logger != null)
            {
                LogCardCreated(_logger, card.ModelName, card.SerialNumber, null);
            }
        }

        return card;
    }

    /// <summary>
    /// Save a SMART check result to a disk card.
    /// </summary>
    public async Task<TestSession> SaveSmartCheckAsync(
        DiskCard card,
        SmartaData smartaData,
        QualityRating rating,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(smartaData);

        var session = new TestSession
        {
            DiskCardId = card.Id,
            SessionId = Guid.NewGuid(),
            TestType = TestType.SmartShort,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Status = TestStatus.Completed,
            IsDestructive = false,
            Result = TestResult.Pass,
            Grade = rating.Grade.ToString(),
            Score = (int)rating.Score,
            HealthAssessment = MapHealthAssessment(rating.Grade.ToString())
        };

        // Add temperature samples if available
        if (smartaData.Temperature.HasValue)
        {
            session.StartTemperature = smartaData.Temperature.Value;
            session.MaxTemperature = smartaData.Temperature.Value;
            session.AverageTemperature = smartaData.Temperature.Value;
            session.TemperatureSamples.Add(new TemperatureSample
            {
                Timestamp = DateTime.UtcNow,
                TemperatureCelsius = smartaData.Temperature.Value,
                Phase = "SMART",
                ProgressPercent = 0
            });
        }

        // Add SMART attribute changes
        session.SmartChanges.Add(new SmartAttributeChange
        {
            AttributeId = 5,
            AttributeName = "Reallocated Sector Count",
            ValueBefore = 0,
            ValueAfter = smartaData.ReallocatedSectorCount ?? 0,
            Change = smartaData.ReallocatedSectorCount ?? 0
        });

        session.SmartChanges.Add(new SmartAttributeChange
        {
            AttributeId = 197,
            AttributeName = "Current Pending Sector Count",
            ValueBefore = 0,
            ValueAfter = smartaData.PendingSectorCount ?? 0,
            Change = smartaData.PendingSectorCount ?? 0
        });

        session.SmartChanges.Add(new SmartAttributeChange
        {
            AttributeId = 198,
            AttributeName = "Uncorrectable Sector Count",
            ValueBefore = 0,
            ValueAfter = smartaData.UncorrectableErrorCount ?? 0,
            Change = smartaData.UncorrectableErrorCount ?? 0
        });

        _dbContext.TestSessions.Add(session);

        // Update disk card
        card.TestCount++;
        card.LastTestedAt = DateTime.UtcNow;
        await UpdateCardGradeAsync(card, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_logger != null)
        {
            LogSmartSaved(_logger, card.Id, session.Grade, null);
        }

        return session;
    }

    /// <summary>
    /// Save a surface test result to a disk card.
    /// </summary>
    public async Task<TestSession> SaveSurfaceTestAsync(
        DiskCard card,
        SurfaceTestResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(result);

        var testType = result.Operation == SurfaceTestOperation.ReadOnly ? TestType.QuickRead :
                       result.Operation == SurfaceTestOperation.WritePattern ? TestType.FullReadWrite :
                       result.Operation == SurfaceTestOperation.WriteZeroFill ? TestType.Sanitization :
                       TestType.SurfaceScan;

        var session = new TestSession
        {
            DiskCardId = card.Id,
            SessionId = Guid.Parse(result.TestId ?? Guid.NewGuid().ToString()),
            TestType = testType,
            StartedAt = result.StartedAtUtc != default ? result.StartedAtUtc : DateTime.UtcNow,
            CompletedAt = result.CompletedAtUtc != default ? result.CompletedAtUtc : DateTime.UtcNow,
            Status = TestStatus.Completed,
            IsDestructive = result.Operation == SurfaceTestOperation.WriteZeroFill,
            BytesWritten = result.Operation == SurfaceTestOperation.ReadOnly ? 0 : result.TotalBytesTested,
            BytesRead = result.TotalBytesTested,
            AverageWriteSpeedMBps = result.AverageSpeedMbps / 1024.0,
            AverageReadSpeedMBps = result.AverageSpeedMbps / 1024.0,
            MaxWriteSpeedMBps = result.PeakSpeedMbps / 1024.0,
            MaxReadSpeedMBps = result.PeakSpeedMbps / 1024.0,
            MinWriteSpeedMBps = result.MinSpeedMbps / 1024.0,
            MinReadSpeedMBps = result.MinSpeedMbps / 1024.0,
            WriteErrors = result.Operation == SurfaceTestOperation.ReadOnly ? 0 : result.ErrorCount,
            ReadErrors = result.ErrorCount,
            VerificationErrors = result.ErrorCount,
            Result = result.ErrorCount == 0 ? TestResult.Pass : TestResult.Fail,
            Grade = CalculateGrade(result),
            Score = CalculateScore(result),
            HealthAssessment = AssessHealth(result)
        };

        // Calculate duration
        session.Duration = session.CompletedAt.Value - session.StartedAt;

        // Add speed samples from result
        foreach (var sample in result.Samples)
        {
            var speedSample = new SpeedSample
            {
                SpeedMBps = sample.ThroughputMbps / 1024.0,
                Timestamp = sample.TimestampUtc
            };

            // Assume write samples for destructive tests
            if (result.Operation == SurfaceTestOperation.WriteZeroFill)
            {
                session.WriteSamples.Add(speedSample);
            }
            else
            {
                session.ReadSamples.Add(speedSample);
            }
        }

        _dbContext.TestSessions.Add(session);

        // Update disk card
        card.TestCount++;
        card.LastTestedAt = DateTime.UtcNow;
        await UpdateCardGradeAsync(card, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_logger != null)
        {
            LogSurfaceSaved(_logger, card.Id, session.Grade, result.ErrorCount, null);
        }

        return session;
    }

    /// <summary>
    /// Save a sanitization result to a disk card.
    /// </summary>
    public async Task<TestSession> SaveSanitizationAsync(
        DiskCard card,
        DiskChecker.Infrastructure.Hardware.Sanitization.SanitizationResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(result);

        var session = new TestSession
        {
            DiskCardId = card.Id,
            SessionId = Guid.NewGuid(),
            TestType = TestType.Sanitization,
            StartedAt = DateTime.UtcNow - result.Duration,
            CompletedAt = DateTime.UtcNow,
            Duration = result.Duration,
            Status = result.Success ? TestStatus.Completed : TestStatus.Failed,
            IsDestructive = true,
            BytesWritten = result.BytesWritten,
            BytesRead = result.BytesRead,
            AverageWriteSpeedMBps = result.WriteSpeedMBps,
            AverageReadSpeedMBps = result.ReadSpeedMBps,
            MaxWriteSpeedMBps = result.WriteSpeedMBps,
            MaxReadSpeedMBps = result.ReadSpeedMBps,
            WriteErrors = result.ErrorsDetected,
            VerificationErrors = result.ErrorsDetected,
            PartitionCreated = result.PartitionCreated,
            PartitionScheme = "GPT",
            WasFormatted = result.Formatted,
            FileSystem = "NTFS",
            VolumeLabel = "SCCM",
            Result = result.Success && result.ErrorsDetected == 0 ? TestResult.Pass : TestResult.Fail,
            Grade = result.ErrorsDetected == 0 ? "A" : result.ErrorsDetected < 10 ? "B" : "F",
            Score = result.ErrorsDetected == 0 ? 100 : Math.Max(0, 100 - result.ErrorsDetected * 5),
            HealthAssessment = result.ErrorsDetected == 0 ? HealthAssessment.Excellent : HealthAssessment.Poor
        };

        _dbContext.TestSessions.Add(session);

        // Update disk card
        card.TestCount++;
        card.LastTestedAt = DateTime.UtcNow;
        await UpdateCardGradeAsync(card, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (_logger != null)
        {
            LogSanitizationSaved(_logger, card.Id, result.Success, result.ErrorsDetected, null);
        }

        return session;
    }

    /// <summary>
    /// Get all test sessions for a disk card.
    /// </summary>
    public async Task<List<TestSession>> GetTestSessionsAsync(int diskCardId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TestSessions
            .Where(t => t.DiskCardId == diskCardId)
            .OrderByDescending(t => t.StartedAt)
            .Include(t => t.TemperatureSamples)
            .Include(t => t.WriteSamples)
            .Include(t => t.ReadSamples)
            .Include(t => t.SmartChanges)
            .Include(t => t.Errors)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Get all disk cards.
    /// </summary>
    public async Task<List<DiskCard>> GetAllCardsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.DiskCards
            .Where(c => !c.IsArchived)
            .OrderByDescending(c => c.LastTestedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Get a disk card by serial number.
    /// </summary>
    public async Task<DiskCard?> GetCardBySerialAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        return await _dbContext.DiskCards
            .FirstOrDefaultAsync(c => c.SerialNumber == serialNumber, cancellationToken);
    }

    /// <summary>
    /// Update the overall grade of a disk card based on all tests.
    /// </summary>
    private async Task UpdateCardGradeAsync(DiskCard card, CancellationToken cancellationToken)
    {
        var sessions = await _dbContext.TestSessions
            .Where(t => t.DiskCardId == card.Id)
            .OrderByDescending(t => t.StartedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (sessions.Count == 0) return;

        // Calculate weighted average score
        var totalWeight = 0.0;
        var weightedScore = 0.0;

        foreach (var session in sessions)
        {
            var weight = session.TestType == TestType.Sanitization ? 2.0 : 1.0;
            totalWeight += weight;
            weightedScore += session.Score * weight;
        }

        card.OverallScore = totalWeight > 0 ? weightedScore / totalWeight : 0;
        card.OverallGrade = ScoreToGrade(card.OverallScore);
    }

    private static string CalculateGrade(SurfaceTestResult result)
    {
        return result.ErrorCount switch
        {
            0 when result.AverageSpeedMbps > 100 => "A+",
            0 when result.AverageSpeedMbps > 50 => "A",
            0 => "B",
            < 5 => "C",
            < 20 => "D",
            _ => "F"
        };
    }

    private static double CalculateScore(SurfaceTestResult result)
    {
        var score = 100.0;

        // Heavy penalty for errors
        score -= result.ErrorCount * 5;

        // Minor penalty for slow speed
        if (result.AverageSpeedMbps < 30)
            score -= 10;

        return Math.Max(0, Math.Min(100, score));
    }

    private static HealthAssessment AssessHealth(SurfaceTestResult result)
    {
        return result.ErrorCount switch
        {
            0 => HealthAssessment.Excellent,
            < 5 => HealthAssessment.Good,
            < 20 => HealthAssessment.Fair,
            < 100 => HealthAssessment.Poor,
            _ => HealthAssessment.Critical
        };
    }

    private static HealthAssessment MapHealthAssessment(string grade)
    {
        return grade?.ToUpperInvariant() switch
        {
            "A+" or "A" => HealthAssessment.Excellent,
            "B" or "B+" or "B-" => HealthAssessment.Good,
            "C" or "C+" or "C-" => HealthAssessment.Fair,
            "D" or "D+" or "D-" => HealthAssessment.Poor,
            _ => HealthAssessment.Critical
        };
    }

    private static string ScoreToGrade(double score)
    {
        return score switch
        {
            >= 95 => "A+",
            >= 90 => "A",
            >= 85 => "A-",
            >= 80 => "B+",
            >= 75 => "B",
            >= 70 => "B-",
            >= 65 => "C+",
            >= 60 => "C",
            >= 55 => "C-",
            >= 50 => "D+",
            >= 45 => "D",
            >= 40 => "D-",
            _ => "F"
        };
    }

    private static string BuildIdentityKey(CoreDriveInfo drive)
    {
        return DriveIdentityResolver.BuildIdentityKey(
            drive.Path,
            drive.SerialNumber ?? string.Empty,
            drive.Name ?? "Unknown",
            null);
    }

    private static string DetermineDiskType(CoreDriveInfo drive)
    {
        var name = (drive.Name ?? "").ToLowerInvariant();
        if (name.Contains("nvme") || name.Contains("ssd"))
            return "SSD";
        if (name.Contains("hdd") || name.Contains("hard"))
            return "HDD";
        return "Unknown";
    }

    private static string DetermineInterfaceType(CoreDriveInfo drive)
    {
        var path = drive.Path.ToLowerInvariant();
        if (path.Contains("nvme"))
            return "NVMe";
        if (drive.IsRemovable)
            return "USB";
        return "SATA";
    }
}