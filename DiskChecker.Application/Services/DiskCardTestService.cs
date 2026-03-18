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

    private sealed class SurfaceScoreBreakdown
    {
        public double Score { get; init; }
        public string Grade { get; init; } = "F";
        public HealthAssessment Health { get; init; } = HealthAssessment.Critical;
        public List<string> Findings { get; init; } = new();
    }

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
        var legacyKey = DriveIdentityResolver.BuildLegacyIdentityKey(
            drive.Path,
            drive.SerialNumber,
            drive.Name ?? "Unknown",
            null);

        var card = await _dbContext.DiskCards
            .FirstOrDefaultAsync(c =>
                c.SerialNumber == serialKey ||
                c.SerialNumber == legacyKey ||
                c.DevicePath == drive.Path,
                cancellationToken);

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
                FirmwareVersion = drive.FirmwareVersion ?? string.Empty,
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

            return card;
        }

        var cardChanged = false;

        if (!string.Equals(card.SerialNumber, serialKey, StringComparison.Ordinal))
        {
            var serialKeyTaken = await _dbContext.DiskCards
                .AnyAsync(c => c.Id != card.Id && c.SerialNumber == serialKey, cancellationToken);

            if (!serialKeyTaken)
            {
                card.SerialNumber = serialKey;
                cardChanged = true;
            }
        }

        if (!string.Equals(card.DevicePath, drive.Path, StringComparison.OrdinalIgnoreCase))
        {
            card.DevicePath = drive.Path;
            cardChanged = true;
        }

        if (!string.Equals(card.ModelName, drive.Name ?? "Unknown", StringComparison.Ordinal))
        {
            card.ModelName = drive.Name ?? "Unknown";
            cardChanged = true;
        }

        if (card.IsArchived)
        {
            ReactivateCardForTesting(card);
            cardChanged = true;
        }

        if (cardChanged)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
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
            HealthAssessment = MapHealthAssessment(rating.Grade.ToString()),
            SmartBefore = smartaData
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
        ReactivateCardForTesting(card);
        card.TestCount++;
        card.LastTestedAt = DateTime.UtcNow;
        card.PowerOnHours = smartaData.PowerOnHours ?? card.PowerOnHours;
        card.PowerCycleCount = smartaData.PowerCycleCount > 0 ? smartaData.PowerCycleCount : card.PowerCycleCount;
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

        var breakdown = BuildSurfaceScoreBreakdown(result);
        if (breakdown.Findings.Count > 0)
        {
            result.Notes = string.Join("; ", breakdown.Findings);
        }

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
            AverageWriteSpeedMBps = result.AverageSpeedMbps,
            AverageReadSpeedMBps = result.AverageSpeedMbps,
            MaxWriteSpeedMBps = result.PeakSpeedMbps,
            MaxReadSpeedMBps = result.PeakSpeedMbps,
            MinWriteSpeedMBps = result.MinSpeedMbps,
            MinReadSpeedMBps = result.MinSpeedMbps,
            WriteErrors = result.Operation == SurfaceTestOperation.ReadOnly ? 0 : result.ErrorCount,
            ReadErrors = result.ErrorCount,
            VerificationErrors = result.ErrorCount,
            Result = result.ErrorCount == 0 ? TestResult.Pass : TestResult.Fail,
            Grade = breakdown.Grade,
            Score = breakdown.Score,
            HealthAssessment = breakdown.Health,
            StartTemperature = result.CurrentTemperatureCelsius,
            MaxTemperature = result.CurrentTemperatureCelsius,
            AverageTemperature = result.CurrentTemperatureCelsius,
            Notes = result.Notes
        };

        // Calculate duration
        session.Duration = session.CompletedAt.Value - session.StartedAt;

        // Add speed samples from result
        foreach (var sample in result.Samples)
        {
            var speedSample = new SpeedSample
            {
                SpeedMBps = sample.ThroughputMbps,
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

        var speedValues = result.Samples
            .Select(s => s.ThroughputMbps)
            .Where(v => v > 0)
            .ToArray();

        var speedStdDev = CalculateStandardDeviation(speedValues);
        session.WriteSpeedStdDev = speedStdDev;
        session.ReadSpeedStdDev = speedStdDev;

        _dbContext.TestSessions.Add(session);

        // Update disk card
        ReactivateCardForTesting(card);
        card.TestCount++;
        card.LastTestedAt = DateTime.UtcNow;
        if (session.SmartBefore?.PowerOnHours is { } poh)
        {
            card.PowerOnHours = poh;
        }

        card.PowerCycleCount = session.SmartBefore?.PowerCycleCount ?? card.PowerCycleCount;
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
        SanitizationResult result,
        IEnumerable<SpeedSample>? writeSamples = null,
        IEnumerable<SpeedSample>? readSamples = null,
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

        if (writeSamples != null)
        {
            session.WriteSamples.AddRange(writeSamples);
        }

        if (readSamples != null)
        {
            session.ReadSamples.AddRange(readSamples);
        }

        _dbContext.TestSessions.Add(session);

        // Update disk card
        ReactivateCardForTesting(card);
        card.TestCount++;
        card.LastTestedAt = DateTime.UtcNow;
        if (session.SmartBefore?.PowerOnHours is { } poh)
        {
            card.PowerOnHours = poh;
        }

        card.PowerCycleCount = session.SmartBefore?.PowerCycleCount ?? card.PowerCycleCount;
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

    private static HealthAssessment MapHealthAssessment(string grade)
    {
        return grade?.ToUpperInvariant() switch
        {
            "A+" or "A" => HealthAssessment.Excellent,
            "B" or "B+" or "B-" => HealthAssessment.Good,
            "C" or "C+" or "C-" => HealthAssessment.Fair,
            "D" or "D+" or "D-" or "E" => HealthAssessment.Poor,
            _ => HealthAssessment.Critical
        };
    }

    private static string ScoreToGrade(double score)
    {
        return score switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 60 => "D",
            _ => "F"
        };
    }

    private static string CalculateGrade(double score)
    {
        return score switch
        {
            >= 92 => "A",
            >= 84 => "B",
            >= 74 => "C",
            >= 62 => "D",
            >= 50 => "E",
            _ => "F"
        };
    }

    private static double CalculateScore(SurfaceTestResult result)
    {
        return BuildSurfaceScoreBreakdown(result).Score;
    }

    private static SurfaceScoreBreakdown BuildSurfaceScoreBreakdown(SurfaceTestResult result)
    {
        var findings = new List<string>();
        var score = 100d;

        var errorWeight = result.Operation == SurfaceTestOperation.ReadOnly ? 10d : 14d;
        if (result.ErrorCount > 0)
        {
            findings.Add($"Nalezené chyby bloků: {result.ErrorCount}");
        }
        score -= result.ErrorCount * errorWeight;

        if (result.PeakSpeedMbps > 0)
        {
            var dropRatio = 1d - (result.MinSpeedMbps / result.PeakSpeedMbps);
            if (dropRatio > 0.35d)
            {
                findings.Add($"Výrazný propad rychlosti (min/max): {result.MinSpeedMbps:F1}/{result.PeakSpeedMbps:F1} MB/s");
            }
            score -= Math.Clamp(dropRatio * 40d, 0d, 30d);
        }

        var speedFloor = GetExpectedMinimumAverageSpeed(result);
        if (speedFloor > 0 && result.AverageSpeedMbps < speedFloor)
        {
            var deficitRatio = 1d - (result.AverageSpeedMbps / speedFloor);
            findings.Add($"Průměrná rychlost pod očekáváním: {result.AverageSpeedMbps:F1} MB/s (cílově > {speedFloor:F0} MB/s)");
            score -= Math.Clamp(deficitRatio * 24d, 0d, 24d);
        }

        var variation = GetSpeedVariationCoefficient(result.Samples);
        if (variation > 0)
        {
            if (IsSolidStateDrive(result))
            {
                if (variation > 0.20d)
                {
                    findings.Add($"SSD/NVMe vykazuje nestabilní průběh rychlosti (CV {variation:P0})");
                }
                score -= Math.Clamp((variation - 0.20d) * 120d, 0d, 24d);
            }
            else
            {
                if (variation > 0.35d)
                {
                    findings.Add($"Nestabilní průběh rychlosti u HDD (CV {variation:P0})");
                }
                score -= Math.Clamp((variation - 0.35d) * 70d, 0d, 12d);
            }
        }

        if (result.CurrentTemperatureCelsius is > 55)
        {
            findings.Add($"Vyšší teplota při testu: {result.CurrentTemperatureCelsius.Value}°C");
            score -= Math.Clamp((result.CurrentTemperatureCelsius.Value - 55) * 1.5d, 0d, 15d);
        }

        if (result.ReallocatedSectors is > 0)
        {
            findings.Add($"SMART varování: přemapované sektory {result.ReallocatedSectors.Value}");
            score -= Math.Clamp(result.ReallocatedSectors.Value * 0.20d, 0d, 12d);
        }

        score = Math.Clamp(score, 0d, 100d);
        var grade = CalculateGrade(score);
        var health = AssessHealth(result, score);

        if (findings.Count == 0)
        {
            findings.Add("Bez významných varování, průběh testu stabilní.");
        }

        return new SurfaceScoreBreakdown
        {
            Score = score,
            Grade = grade,
            Health = health,
            Findings = findings
        };
    }

    private static HealthAssessment AssessHealth(SurfaceTestResult result, double score)
    {
        if (result.ErrorCount > 50 || score < 35)
        {
            return HealthAssessment.Critical;
        }

        if (result.ErrorCount > 0 || score < 55)
        {
            return HealthAssessment.Poor;
        }

        if (score < 72)
        {
            return HealthAssessment.Fair;
        }

        if (score < 88)
        {
            return HealthAssessment.Good;
        }

        return HealthAssessment.Excellent;
    }

    private static bool IsSolidStateDrive(SurfaceTestResult result)
    {
        var descriptor = string.Concat(result.DriveModel, " ", result.DriveInterface);
        return descriptor.Contains("nvme", StringComparison.OrdinalIgnoreCase) ||
               descriptor.Contains("ssd", StringComparison.OrdinalIgnoreCase);
    }

    private static double GetExpectedMinimumAverageSpeed(SurfaceTestResult result)
    {
        if (IsSolidStateDrive(result))
        {
            return 140d;
        }

        return 55d;
    }

    private static double GetSpeedVariationCoefficient(IReadOnlyCollection<SurfaceTestSample> samples)
    {
        if (samples.Count < 5)
        {
            return 0d;
        }

        var values = samples
            .Select(s => s.ThroughputMbps)
            .Where(v => v > 0)
            .ToArray();

        if (values.Length < 5)
        {
            return 0d;
        }

        var average = values.Average();
        if (average <= 0)
        {
            return 0d;
        }

        var stdDev = CalculateStandardDeviation(values);
        return stdDev / average;
    }

    private static double CalculateStandardDeviation(IReadOnlyCollection<double> values)
    {
        if (values.Count < 2)
        {
            return 0d;
        }

        var avg = values.Average();
        var variance = values.Sum(v => Math.Pow(v - avg, 2)) / values.Count;
        return Math.Sqrt(variance);
    }

    private static void ReactivateCardForTesting(DiskCard card)
    {
        if (!card.IsArchived)
        {
            return;
        }

        card.IsArchived = false;
        card.ArchiveReason = null;
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