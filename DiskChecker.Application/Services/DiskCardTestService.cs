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
/// Service for saving test results to disk cards. Ensures all tests (SMART, surface, sanitization) are properly
/// recorded.
/// </summary>
public class DiskCardTestService
{
   private readonly DiskCheckerDbContext _dbContext;
   private readonly IQualityCalculator _qualityCalculator;
   private readonly ILogger<DiskCardTestService>? _logger;
   private readonly ICertificateGenerator _certificateGenerator;

   private const double SignificantSpeedDropRatio = 0.55d;
   private const double CriticalSpeedDropRatio = 0.75d;

   private sealed class SurfaceScoreBreakdown
   {
      public double Score { get; init; }
      public string Grade { get; init; } = "F";
      public HealthAssessment Health { get; init; } = HealthAssessment.Critical;
      public List<string> Findings { get; init; } = new();
   }

   private sealed record PerformanceHistorySnapshot(
       double AverageWriteSpeedMBps,
       double AverageReadSpeedMBps,
       double MaxWriteSpeedMBps,
       double MaxReadSpeedMBps,
       double Score,
       string Grade,
       DateTime StartedAt);

   private sealed record SpeedStabilityAnalysis(
       int SignificantDrops,
       int CriticalDrops,
       double WorstDropRatio,
       double VariationCoefficient,
       double Penalty,
       List<string> Findings);

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

   public DiskCardTestService(
       DiskCheckerDbContext dbContext,
       IQualityCalculator qualityCalculator,
       ILogger<DiskCardTestService>? logger,
       ICertificateGenerator certificateGenerator)
   {
      _dbContext = dbContext;
      _qualityCalculator = qualityCalculator;
      _logger = logger;
      _certificateGenerator = certificateGenerator;
   }

   /// <summary>
   /// Get or create a disk card for a drive.
   /// </summary>
   public async Task<DiskCard> GetOrCreateCardAsync(CoreDriveInfo drive, SmartaData? smartaData = null, CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(drive);

      var serialKey = BuildIdentityKey(drive, smartaData);
      var legacyKey = DriveIdentityResolver.BuildLegacyIdentityKey(
          drive.Path,
          GetPreferredSerialNumber(drive, smartaData),
          GetPreferredModelName(drive, smartaData),
          GetPreferredFirmwareVersion(drive, smartaData));
      var hasReliableIdentity = HasReliableSerialNumber(smartaData?.SerialNumber) || HasReliableSerialNumber(drive.SerialNumber);

      DiskCard? card;
      if(hasReliableIdentity)
      {
         card = await _dbContext.DiskCards
             .FirstOrDefaultAsync(c =>
                 c.SerialNumber == serialKey ||
                 c.SerialNumber == legacyKey,
                 cancellationToken);
      }
      else
      {
         card = await _dbContext.DiskCards
             .FirstOrDefaultAsync(c =>
                 c.SerialNumber == serialKey ||
                 c.SerialNumber == legacyKey ||
                 c.DevicePath == drive.Path,
                 cancellationToken);
      }

      if(card == null)
      {
         card = new DiskCard
         {
            ModelName = GetPreferredModelName(drive, smartaData),
            SerialNumber = serialKey,
            DevicePath = drive.Path,
            DiskType = DetermineDiskType(drive),
            InterfaceType = DetermineInterfaceType(drive),
            Capacity = drive.TotalSize,
            FirmwareVersion = GetPreferredFirmwareVersion(drive, smartaData),
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

         if(_logger != null)
         {
            LogCardCreated(_logger, card.ModelName, card.SerialNumber, null);
         }

         return card;
      }

      var cardChanged = false;

      if(!string.Equals(card.SerialNumber, serialKey, StringComparison.Ordinal))
      {
         var serialKeyTaken = await _dbContext.DiskCards
             .AnyAsync(c => c.Id != card.Id && c.SerialNumber == serialKey, cancellationToken);

         if(!serialKeyTaken)
         {
            card.SerialNumber = serialKey;
            cardChanged = true;
         }
      }

      if(!hasReliableIdentity && !string.Equals(card.DevicePath, drive.Path, StringComparison.OrdinalIgnoreCase))
      {
         card.DevicePath = drive.Path;
         cardChanged = true;
      }
      else if(string.IsNullOrWhiteSpace(card.DevicePath))
      {
         card.DevicePath = drive.Path;
         cardChanged = true;
      }

      var preferredModelName = GetPreferredModelName(drive, smartaData);
      if(!string.Equals(card.ModelName, preferredModelName, StringComparison.Ordinal))
      {
         card.ModelName = preferredModelName;
         cardChanged = true;
      }

      var preferredFirmwareVersion = GetPreferredFirmwareVersion(drive, smartaData);
      if(!string.Equals(card.FirmwareVersion, preferredFirmwareVersion, StringComparison.Ordinal))
      {
         card.FirmwareVersion = preferredFirmwareVersion;
         cardChanged = true;
      }

      if(card.Capacity <= 0 && drive.TotalSize > 0)
      {
         card.Capacity = drive.TotalSize;
         cardChanged = true;
      }

      if(card.IsArchived)
      {
         ReactivateCardForTesting(card);
         cardChanged = true;
      }

      if(cardChanged)
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
      if(smartaData.Temperature.HasValue)
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

      if(_logger != null)
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
       SmartaData? smartaData = null,
       CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(card);
      ArgumentNullException.ThrowIfNull(result);

      var testType = result.Operation == SurfaceTestOperation.ReadOnly ? TestType.QuickRead :
                     result.Operation == SurfaceTestOperation.WritePattern ? TestType.FullReadWrite :
                     result.Operation == SurfaceTestOperation.WriteZeroFill ? TestType.Sanitization :
                     TestType.SurfaceScan;

      var breakdown = BuildCombinedSurfaceScoreBreakdown(result, smartaData);
      if(breakdown.Findings.Count > 0)
      {
         result.Notes = string.Join("; ", breakdown.Findings);
      }

      var orderedSamples = result.Samples
          .Where(s => s.ThroughputMbps > 0)
          .OrderBy(s => s.TimestampUtc)
          .ToList();

      List<SpeedSample> writeSamples;
      List<SpeedSample> readSamples;
      if(result.Operation == SurfaceTestOperation.ReadOnly)
      {
         writeSamples = [];
         readSamples = orderedSamples
             .Select(s => new SpeedSample
             {
                Timestamp = s.TimestampUtc,
                ProgressPercent = s.ProgressPercent,
                SpeedMBps = s.ThroughputMbps,
                BytesProcessed = s.OffsetBytes
             })
             .ToList();
      }
      else
      {
         var splitIndex = orderedSamples.Count / 2;
         writeSamples = orderedSamples
             .Take(splitIndex)
             .Select(s => new SpeedSample
             {
                Timestamp = s.TimestampUtc,
                ProgressPercent = s.ProgressPercent,
                SpeedMBps = s.ThroughputMbps,
                BytesProcessed = s.OffsetBytes
             })
             .ToList();
         readSamples = orderedSamples
             .Skip(splitIndex)
             .Select(s => new SpeedSample
             {
                Timestamp = s.TimestampUtc,
                ProgressPercent = s.ProgressPercent,
                SpeedMBps = s.ThroughputMbps,
                BytesProcessed = s.OffsetBytes
             })
             .ToList();
      }

      var temperatureSamples = orderedSamples
          .Where(s => s.TemperatureCelsius.HasValue)
          .GroupBy(s => (int)Math.Floor(Math.Clamp(s.ProgressPercent, 0, 100)))
          .Select(g => g.OrderByDescending(x => x.TimestampUtc).First())
          .OrderBy(s => s.ProgressPercent)
          .Select(s => new TemperatureSample
          {
             Timestamp = s.TimestampUtc,
             TemperatureCelsius = s.TemperatureCelsius ?? 0,
             ProgressPercent = s.ProgressPercent,
             Phase = "Surface"
          })
          .ToList();

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
         Notes = result.Notes,
         SmartBefore = smartaData,
         WriteSamples = writeSamples,
         ReadSamples = readSamples,
         TemperatureSamples = temperatureSamples
      };

      if(temperatureSamples.Count > 0)
      {
         session.StartTemperature = temperatureSamples.First().TemperatureCelsius;
         session.MaxTemperature = temperatureSamples.Max(s => s.TemperatureCelsius);
         session.AverageTemperature = temperatureSamples.Average(s => s.TemperatureCelsius);
      }

      ApplySmartSnapshot(session, smartaData);

      _dbContext.TestSessions.Add(session);

      // Update disk card
      ReactivateCardForTesting(card);
      card.TestCount++;
      card.LastTestedAt = DateTime.UtcNow;
      if(session.SmartBefore?.PowerOnHours is { } poh)
      {
         card.PowerOnHours = poh;
      }

      card.PowerCycleCount = session.SmartBefore?.PowerCycleCount ?? card.PowerCycleCount;
      await UpdateCardGradeAsync(card, cancellationToken);

      await _dbContext.SaveChangesAsync(cancellationToken);

      if(_logger != null)
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
       SmartaData? smartaData = null,
       SmartaData? smartAfter = null,
       CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(card);
      ArgumentNullException.ThrowIfNull(result);

      var persistedWriteSamples = (writeSamples ?? Enumerable.Empty<SpeedSample>())
          .Where(s => s.SpeedMBps > 0)
          .OrderBy(s => s.ProgressPercent)
          .Select(s => new SpeedSample
          {
             ProgressPercent = s.ProgressPercent,
             SpeedMBps = s.SpeedMBps
          })
          .ToList();
      var persistedReadSamples = (readSamples ?? Enumerable.Empty<SpeedSample>())
          .Where(s => s.SpeedMBps > 0)
          .OrderBy(s => s.ProgressPercent)
          .Select(s => new SpeedSample
          {
             ProgressPercent = s.ProgressPercent,
             SpeedMBps = s.SpeedMBps
          })
          .ToList();
      var historicalSessions = await LoadRecentPerformanceHistoryAsync(card.Id, cancellationToken);
      var breakdown = BuildSanitizationScoreBreakdown(result, persistedWriteSamples, persistedReadSamples, smartaData, result.SmartAfter, historicalSessions);
      var avgWriteSpeed = persistedWriteSamples.Count > 0
          ? persistedWriteSamples.Average(s => s.SpeedMBps)
          : result.WriteSpeedMBps;
      var maxWriteSpeed = persistedWriteSamples.Count > 0
          ? persistedWriteSamples.Max(s => s.SpeedMBps)
          : result.WriteSpeedMBps;
      var minWriteSpeed = persistedWriteSamples.Count > 0
          ? persistedWriteSamples.Min(s => s.SpeedMBps)
          : result.WriteSpeedMBps;
      var writeStdDev = persistedWriteSamples.Count > 1
          ? CalculateStandardDeviation(persistedWriteSamples.Select(s => s.SpeedMBps).ToArray())
          : 0d;

      var avgReadSpeed = persistedReadSamples.Count > 0
          ? persistedReadSamples.Average(s => s.SpeedMBps)
          : result.ReadSpeedMBps;
      var maxReadSpeed = persistedReadSamples.Count > 0
          ? persistedReadSamples.Max(s => s.SpeedMBps)
          : result.ReadSpeedMBps;
      var minReadSpeed = persistedReadSamples.Count > 0
          ? persistedReadSamples.Min(s => s.SpeedMBps)
          : result.ReadSpeedMBps;
      var readStdDev = persistedReadSamples.Count > 1
          ? CalculateStandardDeviation(persistedReadSamples.Select(s => s.SpeedMBps).ToArray())
          : 0d;

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
         AverageWriteSpeedMBps = avgWriteSpeed,
         AverageReadSpeedMBps = avgReadSpeed,
         MaxWriteSpeedMBps = maxWriteSpeed,
         MaxReadSpeedMBps = maxReadSpeed,
         MinWriteSpeedMBps = minWriteSpeed,
         MinReadSpeedMBps = minReadSpeed,
         WriteSpeedStdDev = writeStdDev,
         ReadSpeedStdDev = readStdDev,
         WriteDuration = result.Duration,
         ReadDuration = result.Duration,
         WriteErrors = result.ErrorsDetected,
         ReadErrors = 0,
         VerificationErrors = 0,
         PartitionCreated = result.PartitionCreated,
         PartitionScheme = "GPT",
         WasFormatted = result.Formatted,
         FileSystem = "NTFS",
         VolumeLabel = "SCCM",
         Result = result.Success && result.ErrorsDetected == 0 ? TestResult.Pass : TestResult.Fail,
         Grade = breakdown.Grade,
         Score = breakdown.Score,
         HealthAssessment = breakdown.Health,
         Notes = breakdown.Findings.Count > 0 ? string.Join("; ", breakdown.Findings) : null,
         SmartBefore = smartaData,
         SmartAfter = smartAfter,
         WriteSamples = persistedWriteSamples,
         ReadSamples = persistedReadSamples
      };

      foreach(var errorDetail in result.ErrorDetails)
      {
         session.Errors.Add(new TestError
         {
            Timestamp = DateTime.UtcNow,
            ErrorCode = string.IsNullOrWhiteSpace(errorDetail.ErrorCode) ? "SANITIZE" : errorDetail.ErrorCode,
            Message = errorDetail.Message,
            Phase = errorDetail.Phase,
            IsCritical = true,
            Details = errorDetail.Details
         });
      }

      ApplySmartSnapshot(session, smartaData);

      _dbContext.TestSessions.Add(session);

      // Update disk card
      ReactivateCardForTesting(card);
      card.TestCount++;
      card.LastTestedAt = DateTime.UtcNow;
      if(session.SmartBefore?.PowerOnHours is { } poh)
      {
         card.PowerOnHours = poh;
      }

      card.PowerCycleCount = session.SmartBefore?.PowerCycleCount ?? card.PowerCycleCount;
      await UpdateCardGradeAsync(card, cancellationToken);

      await _dbContext.SaveChangesAsync(cancellationToken);

      session.ChartImagePath = await _certificateGenerator.GenerateAndStoreChartImageAsync(session, cancellationToken);
      if(!string.IsNullOrWhiteSpace(session.ChartImagePath))
      {
         _dbContext.TestSessions.Update(session);
         await _dbContext.SaveChangesAsync(cancellationToken);
      }

      if(_logger != null)
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

      if(sessions.Count == 0) return;

      var totalWeight = 0.0;
      var weightedScore = 0.0;

      foreach(var session in sessions)
      {
         var weight = session.TestType == TestType.Sanitization ? 2.0 : 1.0;
         totalWeight += weight;
         weightedScore += session.Score * weight;
      }

      card.OverallScore = totalWeight > 0 ? weightedScore / totalWeight : 0;

      var calculatedGrade = ScoreToGrade(card.OverallScore);
      if(sessions.Any(s => string.Equals(NormalizeGrade(s.Grade), "F", StringComparison.Ordinal)))
      {
         card.OverallScore = Math.Min(card.OverallScore, 49);
         card.OverallGrade = "F";
         return;
      }

      if(sessions.Any(s => string.Equals(NormalizeGrade(s.Grade), "E", StringComparison.Ordinal)))
      {
         card.OverallScore = Math.Min(card.OverallScore, 59);
         card.OverallGrade = GetWorseGrade(calculatedGrade, "E");
         return;
      }

      card.OverallGrade = calculatedGrade;
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
         >= 92 => "A",
         >= 84 => "B",
         >= 74 => "C",
         >= 62 => "D",
         >= 50 => "E",
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

   private SurfaceScoreBreakdown BuildCombinedSurfaceScoreBreakdown(SurfaceTestResult result, SmartaData? smartaData)
   {
      var breakdown = BuildSurfaceScoreBreakdown(result);
      return ApplySmartQuality(breakdown, smartaData);
   }

   private static SurfaceScoreBreakdown BuildSurfaceScoreBreakdown(SurfaceTestResult result)
   {
      var findings = new List<string>();
      var score = 100d;

      var errorWeight = result.Operation == SurfaceTestOperation.ReadOnly ? 10d : 14d;
      if(result.ErrorCount > 0)
      {
         findings.Add($"Nalezené chyby bloků: {result.ErrorCount}");
      }
      score -= result.ErrorCount * errorWeight;

      if(result.PeakSpeedMbps > 0)
      {
         var dropRatio = 1d - (result.MinSpeedMbps / result.PeakSpeedMbps);
         if(dropRatio > 0.35d)
         {
            findings.Add($"Výrazný propad rychlosti (min/max): {result.MinSpeedMbps:F1}/{result.PeakSpeedMbps:F1} MB/s");
         }
         score -= Math.Clamp(dropRatio * 40d, 0d, 30d);
      }

      var speedFloor = GetExpectedMinimumAverageSpeed(result);
      if(speedFloor > 0 && result.AverageSpeedMbps < speedFloor)
      {
         var deficitRatio = 1d - (result.AverageSpeedMbps / speedFloor);
         findings.Add($"Průměrná rychlost pod očekáváním: {result.AverageSpeedMbps:F1} MB/s (cílově > {speedFloor:F0} MB/s)");
         score -= Math.Clamp(deficitRatio * 24d, 0d, 24d);
      }

      var variation = GetSpeedVariationCoefficient(result.Samples);
      if(variation > 0)
      {
         if(IsSolidStateDrive(result))
         {
            if(variation > 0.20d)
            {
               findings.Add($"SSD/NVMe vykazuje nestabilní průběh rychlosti (CV {variation:P0})");
            }
            score -= Math.Clamp((variation - 0.20d) * 120d, 0d, 24d);
         }
         else
         {
            if(variation > 0.35d)
            {
               findings.Add($"Nestabilní průběh rychlosti u HDD (CV {variation:P0})");
            }
            score -= Math.Clamp((variation - 0.35d) * 70d, 0d, 12d);
         }
      }

      var thermalPenalty = GetThermalThrottlingPenalty(result);
      if(thermalPenalty.Penalty > 0)
      {
         findings.Add(thermalPenalty.Finding);
         score -= thermalPenalty.Penalty;
      }

      if(result.CurrentTemperatureCelsius is > 55)
      {
         findings.Add($"Vyšší teplota při testu: {result.CurrentTemperatureCelsius.Value}°C");
         score -= Math.Clamp((result.CurrentTemperatureCelsius.Value - 55) * 1.5d, 0d, 15d);
      }

      if(result.ReallocatedSectors is > 0)
      {
         findings.Add($"SMART varování: přemapované sektory {result.ReallocatedSectors.Value}");
         score -= Math.Clamp(result.ReallocatedSectors.Value * 0.20d, 0d, 12d);
      }

      score = Math.Clamp(score, 0d, 100d);
      var grade = CalculateGrade(score);
      var health = AssessHealth(result, score);

      if(findings.Count == 0)
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
      if(result.ErrorCount > 50 || score < 35)
      {
         return HealthAssessment.Critical;
      }

      if(result.ErrorCount > 0 || score < 55)
      {
         return HealthAssessment.Poor;
      }

      if(score < 72)
      {
         return HealthAssessment.Fair;
      }

      if(score < 88)
      {
         return HealthAssessment.Good;
      }

      return HealthAssessment.Excellent;
   }

   private static bool IsSolidStateDrive(SurfaceTestResult result)
   {
      if(result.Technology == DriveTechnology.Nvme || result.Technology == DriveTechnology.Ssd)
      {
         return true;
      }

      if(result.Technology == DriveTechnology.Hdd)
      {
         return false;
      }

      var descriptor = string.Concat(result.DriveModel, " ", result.DriveInterface);
      return descriptor.Contains("nvme", StringComparison.OrdinalIgnoreCase) ||
             descriptor.Contains("ssd", StringComparison.OrdinalIgnoreCase) ||
             descriptor.Contains("solid", StringComparison.OrdinalIgnoreCase) ||
             descriptor.Contains("flash", StringComparison.OrdinalIgnoreCase);
   }

   /// <summary>
   /// Calculates additional penalty when speed drop correlates with temperature rise.
   /// </summary>
   private static (double Penalty, string Finding) GetThermalThrottlingPenalty(SurfaceTestResult result)
   {
      if(!IsSolidStateDrive(result) || result.Samples.Count < 12)
      {
         return (0d, string.Empty);
      }

      var ordered = result.Samples
          .Where(s => s.ThroughputMbps > 0)
          .OrderBy(s => s.ProgressPercent)
          .ToList();

      if(ordered.Count < 12)
      {
         return (0d, string.Empty);
      }

      var head = ordered.Take(Math.Max(4, ordered.Count / 5)).ToList();
      var tail = ordered.Skip(Math.Max(0, ordered.Count - Math.Max(4, ordered.Count / 5))).ToList();
      var headSpeed = head.Average(s => s.ThroughputMbps);
      var tailSpeed = tail.Average(s => s.ThroughputMbps);
      if(headSpeed <= 0)
      {
         return (0d, string.Empty);
      }

      var dropRatio = 1d - (tailSpeed / headSpeed);
      var headTemp = head.Where(s => s.TemperatureCelsius.HasValue).Select(s => (double)s.TemperatureCelsius!.Value).DefaultIfEmpty().Average();
      var tailTemp = tail.Where(s => s.TemperatureCelsius.HasValue).Select(s => (double)s.TemperatureCelsius!.Value).DefaultIfEmpty().Average();
      var tempRise = tailTemp - headTemp;

      if(dropRatio < 0.15d || tempRise < 5d)
      {
         return (0d, string.Empty);
      }

      var penalty = Math.Clamp((dropRatio - 0.15d) * 70d + (tempRise - 5d) * 1.2d, 0d, 18d);
      return (penalty, $"Pravděpodobný SSD thermal throttling: pokles rychlosti {dropRatio:P0} při nárůstu teploty o {tempRise:F0}°C.");
   }

   private static double GetExpectedMinimumAverageSpeed(SurfaceTestResult result)
   {
      if(IsSolidStateDrive(result))
      {
         return 140d;
      }

      return 55d;
   }

   private static double GetSpeedVariationCoefficient(IReadOnlyCollection<SurfaceTestSample> samples)
   {
      if(samples.Count < 5)
      {
         return 0d;
      }

      var values = samples
          .Select(s => s.ThroughputMbps)
          .Where(v => v > 0)
          .ToArray();

      if(values.Length < 5)
      {
         return 0d;
      }

      var average = values.Average();
      if(average <= 0)
      {
         return 0d;
      }

      var stdDev = CalculateStandardDeviation(values);
      return stdDev / average;
   }

   private static double CalculateStandardDeviation(IReadOnlyCollection<double> values)
   {
      if(values.Count < 2)
      {
         return 0d;
      }

      var avg = values.Average();
      var variance = values.Sum(v => Math.Pow(v - avg, 2)) / values.Count;
      return Math.Sqrt(variance);
   }

   private SurfaceScoreBreakdown BuildSanitizationScoreBreakdown(
       SanitizationResult result,
       IReadOnlyList<SpeedSample> writeSamples,
       IReadOnlyList<SpeedSample> readSamples,
       SmartaData? smartaData,
       SmartaData? smartAfter,
       IReadOnlyList<PerformanceHistorySnapshot> history)
   {
      ArgumentNullException.ThrowIfNull(result);
      ArgumentNullException.ThrowIfNull(writeSamples);
      ArgumentNullException.ThrowIfNull(readSamples);
      ArgumentNullException.ThrowIfNull(history);

      var findings = new List<string>();
      var score = 100d;
      var hasSmartContext = smartaData != null;
      var driveLooksSolidState = LooksLikeSolidState(smartaData);

      if(!result.Success)
      {
         findings.Add(string.IsNullOrWhiteSpace(result.ErrorMessage)
             ? "Sanitizační test nebyl dokončen úspěšně."
             : $"Sanitizační test selhal: {result.ErrorMessage}");
         score = 0d;
      }

      if(result.ErrorsDetected > 0)
      {
         findings.Add($"Při sanitizaci bylo zjištěno {result.ErrorsDetected} chyb.");
         score -= result.ErrorsDetected * 12d;
      }

      score -= ApplySanitizationDiagnostics(result, findings, driveLooksSolidState);

      if(writeSamples.Count > 0)
      {
         var expectedWriteFloor = driveLooksSolidState ? 140d : 55d;
         if(result.WriteSpeedMBps < expectedWriteFloor)
         {
            var deficitRatio = 1d - (result.WriteSpeedMBps / expectedWriteFloor);
            findings.Add($"Průměrná rychlost zápisu je pod očekáváním: {result.WriteSpeedMBps:F1} MB/s (cílově alespoň {expectedWriteFloor:F0} MB/s).");
            score -= Math.Clamp(deficitRatio * 28d, 0d, 28d);
         }
      }

      if(driveLooksSolidState)
      {
         score -= ApplySsdCacheCollapseAnalysis(writeSamples, findings);
      }

      if(readSamples.Count > 0)
      {
         var expectedReadFloor = driveLooksSolidState ? 160d : 65d;
         if(result.ReadSpeedMBps < expectedReadFloor)
         {
            var deficitRatio = 1d - (result.ReadSpeedMBps / expectedReadFloor);
            findings.Add($"Průměrná rychlost čtení je pod očekáváním: {result.ReadSpeedMBps:F1} MB/s (cílově alespoň {expectedReadFloor:F0} MB/s).");
            score -= Math.Clamp(deficitRatio * 24d, 0d, 24d);
         }
      }

      if (smartaData != null && result.SmartAfter != null)
      {
          var smartDeltaPenalty = ApplySmartDeltaAfterSanitization(smartaData, result.SmartAfter, findings);
          score -= smartDeltaPenalty;
      }

      if(hasSmartContext && history.Count > 0)
      {
         var historyPenalty = ApplyHistoricalContext(history, result, findings);
         score -= historyPenalty;
      }
      else if(!hasSmartContext)
      {
         findings.Add("SMART data nejsou dostupná, hodnocení vychází pouze z průběhu samotného testu.");
      }

      score = Math.Clamp(score, 0d, 100d);

      var health = !result.Success
          ? HealthAssessment.Critical
          : writeSamples.Any(s => s.SpeedMBps < 10)
              ? HealthAssessment.Poor
              : result.ErrorsDetected == 0 && score >= 88
                  ? HealthAssessment.Excellent
                  : score >= 72
                      ? HealthAssessment.Good
                      : score >= 55
                          ? HealthAssessment.Fair
                          : HealthAssessment.Poor;

      var breakdown = new SurfaceScoreBreakdown
      {
         Score = score,
         Grade = !result.Success ? "F" : CalculateGrade(score),
         Health = health,
         Findings = findings
      };

      if(breakdown.Findings.Count == 0)
      {
         breakdown.Findings.Add("Sanitizace proběhla bez chyb a průběh výkonu byl stabilní.");
      }

      return smartaData == null ? breakdown : ApplySmartQuality(breakdown, smartaData);
   }

   private static SpeedStabilityAnalysis AnalyzeSpeedStability(
       IReadOnlyList<SpeedSample> samples,
       string phaseLabel,
       bool isSolidState)
   {
      if(samples.Count < 8)
      {
         return new SpeedStabilityAnalysis(0, 0, 0d, 0d, 0d, []);
      }

      var ordered = samples
          .Where(s => s.SpeedMBps > 0)
          .OrderBy(s => s.ProgressPercent)
          .ToList();
      if(ordered.Count < 8)
      {
         return new SpeedStabilityAnalysis(0, 0, 0d, 0d, 0d, []);
      }

      var values = ordered.Select(s => s.SpeedMBps).ToArray();
      var findings = new List<string>();
      var significantDrops = 0;
      var criticalDrops = 0;
      var worstDropRatio = 0d;

      for(var i = 4; i < values.Length; i++)
      {
         var baseline = values.Skip(Math.Max(0, i - 4)).Take(4).Average();
         if(baseline <= 0)
         {
            continue;
         }

         var dropRatio = 1d - (values[i] / baseline);
         if(dropRatio >= SignificantSpeedDropRatio)
         {
            significantDrops++;
            worstDropRatio = Math.Max(worstDropRatio, dropRatio);
         }

         if(dropRatio >= CriticalSpeedDropRatio)
         {
            criticalDrops++;
         }
      }

      var variation = GetSpeedVariationCoefficient(ordered.Select(s => new SurfaceTestSample { ThroughputMbps = s.SpeedMBps }).ToArray());
      var penalty = 0d;

      if(significantDrops > 0)
      {
         findings.Add($"Byly detekovány výrazné propady rychlosti během {phaseLabel}: {significantDrops}x, nejhorší pokles {worstDropRatio:P0}.");
         penalty += Math.Min(isSolidState ? 26d : 18d, significantDrops * (isSolidState ? 4.5d : 3d));
      }

      if(criticalDrops > 0)
      {
         findings.Add($"Kritické propady rychlosti během {phaseLabel}: {criticalDrops}x. To může ukazovat na vážnou nestabilitu média nebo řadiče.");
         penalty += Math.Min(isSolidState ? 24d : 16d, criticalDrops * (isSolidState ? 8d : 5d));
      }

      var variationLimit = isSolidState ? 0.22d : 0.35d;
      if(variation > variationLimit)
      {
         findings.Add($"Průběh {phaseLabel} je nestabilní (CV {variation:P0}).");
         penalty += Math.Clamp((variation - variationLimit) * (isSolidState ? 110d : 70d), 0d, isSolidState ? 18d : 10d);
      }

      return new SpeedStabilityAnalysis(significantDrops, criticalDrops, worstDropRatio, variation, penalty, findings);
   }

   private static bool LooksLikeSolidState(SmartaData? smartaData)
   {
      if(smartaData == null)
      {
         return false;
      }

      var descriptor = string.Concat(smartaData.DeviceType, " ", smartaData.DeviceModel, " ", smartaData.FirmwareVersion);
      return descriptor.Contains("nvme", StringComparison.OrdinalIgnoreCase) ||
             descriptor.Contains("ssd", StringComparison.OrdinalIgnoreCase) ||
             smartaData.AvailableSpare.HasValue ||
             smartaData.PercentageUsed.HasValue ||
             smartaData.MediaErrors.HasValue;
   }

   private static double ApplyHistoricalContext(
       IReadOnlyList<PerformanceHistorySnapshot> history,
       SanitizationResult result,
       List<string> findings)
   {
      if(history.Count == 0)
      {
         return 0d;
      }

      var comparableHistory = history
          .Where(h => h.AverageWriteSpeedMBps > 0 && h.AverageReadSpeedMBps > 0)
          .ToList();
      if(comparableHistory.Count == 0)
      {
         return 0d;
      }

      var historicalWrite = comparableHistory.Average(h => h.AverageWriteSpeedMBps);
      var historicalRead = comparableHistory.Average(h => h.AverageReadSpeedMBps);
      var penalty = 0d;

      if(historicalWrite > 0 && result.WriteSpeedMBps > 0)
      {
         var dropRatio = 1d - (result.WriteSpeedMBps / historicalWrite);
         if(dropRatio > 0.30d)
         {
            findings.Add($"Aktuální zápis je výrazně pomalejší než historie disku: {result.WriteSpeedMBps:F1} MB/s vs. historicky {historicalWrite:F1} MB/s.");
            penalty += Math.Clamp((dropRatio - 0.30d) * 40d, 0d, 14d);
         }
      }

      if(historicalRead > 0 && result.ReadSpeedMBps > 0)
      {
         var dropRatio = 1d - (result.ReadSpeedMBps / historicalRead);
         if(dropRatio > 0.30d)
         {
            findings.Add($"Aktuální čtení je výrazně pomalejší než historie disku: {result.ReadSpeedMBps:F1} MB/s vs. historicky {historicalRead:F1} MB/s.");
            penalty += Math.Clamp((dropRatio - 0.30d) * 34d, 0d, 12d);
         }
      }

      var degradedHistory = comparableHistory.Count(h => GradeSeverity(h.Grade) >= GradeSeverity("D"));
      if(degradedHistory >= 2)
      {
         findings.Add($"Historie disku obsahuje {degradedHistory} slabší výsledky (D nebo horší), což zvyšuje riziko dlouhodobé degradace.");
         penalty += Math.Min(10d, degradedHistory * 2.5d);
      }

      return penalty;
   }

   private async Task<List<PerformanceHistorySnapshot>> LoadRecentPerformanceHistoryAsync(int diskCardId, CancellationToken cancellationToken)
   {
      return await _dbContext.TestSessions
          .AsNoTracking()
          .Where(t => t.DiskCardId == diskCardId && t.Status == TestStatus.Completed)
          .OrderByDescending(t => t.StartedAt)
          .Take(6)
          .Select(t => new PerformanceHistorySnapshot(
              t.AverageWriteSpeedMBps,
              t.AverageReadSpeedMBps,
              t.MaxWriteSpeedMBps,
              t.MaxReadSpeedMBps,
              t.Score,
              t.Grade,
              t.StartedAt))
          .ToListAsync(cancellationToken);
   }

   private SurfaceScoreBreakdown ApplySmartQuality(SurfaceScoreBreakdown breakdown, SmartaData? smartaData)
   {
      if(smartaData == null)
      {
         return breakdown;
      }

      var findings = new List<string>(breakdown.Findings);
      var smartRating = _qualityCalculator.CalculateQuality(smartaData);
      var score = Math.Min(breakdown.Score, smartRating.Score);
      var grade = breakdown.Grade;
      var health = breakdown.Health;

      foreach(var warning in smartRating.Warnings.Distinct(StringComparer.Ordinal))
      {
         findings.Add($"SMART: {warning}");
      }

      if(HasSmartFailure(smartaData))
      {
         findings.Add("SMART hlásí selhání disku. Výsledná známka je degradována na F.");
         return new SurfaceScoreBreakdown
         {
            Score = Math.Min(score, 49d),
            Grade = "F",
            Health = HealthAssessment.Critical,
            Findings = findings
         };
      }

      grade = GetWorseGrade(grade, smartRating.Grade.ToString());
      health = GetWorseHealth(health, MapHealthAssessment(grade));

      if(HasSmartPrefail(smartaData))
      {
         findings.Add("Byl detekován závažný SMART pre-fail stav. Výsledná známka je degradována maximálně na E.");
         grade = GetWorseGrade(grade, "E");
         score = Math.Min(score, 59d);
         health = GetWorseHealth(health, HealthAssessment.Poor);
      }

      return new SurfaceScoreBreakdown
      {
         Score = score,
         Grade = grade,
         Health = health,
         Findings = findings
      };
   }

   private static void ReactivateCardForTesting(DiskCard card)
   {
      if(!card.IsArchived)
      {
         return;
      }

      card.IsArchived = false;
      card.ArchiveReason = null;
   }

   private static void ApplySmartSnapshot(TestSession session, SmartaData? smartaData)
   {
      if(smartaData == null)
      {
         return;
      }

      session.SmartBefore = smartaData;

      if(smartaData.Temperature is > 0)
      {
         session.StartTemperature ??= smartaData.Temperature.Value;
         session.MaxTemperature = Math.Max(session.MaxTemperature ?? smartaData.Temperature.Value, smartaData.Temperature.Value);
         session.AverageTemperature ??= smartaData.Temperature.Value;
      }

      AddSmartAttributeChange(session, 5, "Reallocated Sector Count", smartaData.ReallocatedSectorCount);
      AddSmartAttributeChange(session, 197, "Current Pending Sector Count", smartaData.PendingSectorCount);
      AddSmartAttributeChange(session, 198, "Uncorrectable Sector Count", smartaData.UncorrectableErrorCount);
      AddSmartAttributeChange(session, 9, "Power-On Hours", smartaData.PowerOnHours);

      if(smartaData.PowerCycleCount > 0)
      {
         AddSmartAttributeChange(session, 12, "Power Cycle Count", smartaData.PowerCycleCount);
      }

      AddSmartAttributeChange(session, 177, "Wear Leveling Count", smartaData.WearLevelingCount);
      AddSmartAttributeChange(session, 232, "Available Spare", smartaData.AvailableSpare);
      AddSmartAttributeChange(session, 233, "Percentage Used", smartaData.PercentageUsed);
      AddSmartAttributeChange(session, 187, "Media Errors", smartaData.MediaErrors);
   }

   private static void AddSmartAttributeChange(TestSession session, int attributeId, string attributeName, int? value)
   {
      if(!value.HasValue || session.SmartChanges.Any(c => c.AttributeId == attributeId))
      {
         return;
      }

      session.SmartChanges.Add(new SmartAttributeChange
      {
         AttributeId = attributeId,
         AttributeName = attributeName,
         ValueBefore = 0,
         ValueAfter = value.Value,
         Change = value.Value
      });
   }

   private static bool HasReliableSerialNumber(string? serialNumber)
   {
      return DriveIdentityResolver.IsReliableSerialNumber(serialNumber);
   }

   private static string GetPreferredSerialNumber(CoreDriveInfo drive, SmartaData? smartaData)
   {
      if(HasReliableSerialNumber(smartaData?.SerialNumber))
      {
         return DriveIdentityResolver.NormalizeSerial(smartaData!.SerialNumber);
      }

      if(HasReliableSerialNumber(drive.SerialNumber))
      {
         return DriveIdentityResolver.NormalizeSerial(drive.SerialNumber);
      }

      return string.Empty;
   }

   private static string GetPreferredModelName(CoreDriveInfo drive, SmartaData? smartaData)
   {
      if(!string.IsNullOrWhiteSpace(smartaData?.DeviceModel))
      {
         return smartaData.DeviceModel.Trim();
      }

      return drive.Name ?? "Unknown";
   }

   private static string GetPreferredFirmwareVersion(CoreDriveInfo drive, SmartaData? smartaData)
   {
      if(!string.IsNullOrWhiteSpace(smartaData?.FirmwareVersion))
      {
         return smartaData.FirmwareVersion.Trim();
      }

      return drive.FirmwareVersion ?? string.Empty;
   }

   private static string BuildIdentityKey(CoreDriveInfo drive, SmartaData? smartaData)
   {
      return DriveIdentityResolver.BuildIdentityKey(
          drive.Path,
          GetPreferredSerialNumber(drive, smartaData),
          GetPreferredModelName(drive, smartaData),
          GetPreferredFirmwareVersion(drive, smartaData));
   }

   private static bool HasSmartFailure(SmartaData smartaData)
   {
      return smartaData.Attributes.Any(a => !a.IsOk && !string.IsNullOrWhiteSpace(a.WhenFailed));
   }

   private static bool HasSmartPrefail(SmartaData smartaData)
   {
      return !smartaData.IsHealthy ||
             smartaData.ReallocatedSectorCount is > 0 ||
             smartaData.PendingSectorCount is > 0 ||
             smartaData.UncorrectableErrorCount is > 0 ||
             smartaData.MediaErrors is > 0 ||
             smartaData.AvailableSpare is < 10 ||
             smartaData.PercentageUsed is > 90;
   }

   private static HealthAssessment GetWorseHealth(HealthAssessment current, HealthAssessment candidate)
   {
      return Severity(candidate) > Severity(current) ? candidate : current;
   }

   private static string GetWorseGrade(string first, string second)
   {
      return GradeSeverity(NormalizeGrade(first)) >= GradeSeverity(NormalizeGrade(second)) ? second : first;
   }

   private static int GradeSeverity(string grade)
   {
      return NormalizeGrade(grade) switch
      {
         "F" => 6,
         "E" => 5,
         "D" => 4,
         "C" => 3,
         "B" => 2,
         _ => 1
      };
   }

   private static string NormalizeGrade(string? grade)
   {
      return string.IsNullOrWhiteSpace(grade) ? "F" : grade.Trim().ToUpperInvariant();
   }

   private static int Severity(HealthAssessment assessment)
   {
      return assessment switch
      {
         HealthAssessment.Critical => 5,
         HealthAssessment.Poor => 4,
         HealthAssessment.Fair => 3,
         HealthAssessment.Good => 2,
         HealthAssessment.Excellent => 1,
         _ => 0
      };
   }

   private static string DetermineDiskType(CoreDriveInfo drive)
   {
      var name = (drive.Name ?? string.Empty).ToLowerInvariant();
      if(name.Contains("nvme") || name.Contains("ssd"))
         return "SSD";
      if(name.Contains("hdd") || name.Contains("hard"))
         return "HDD";
      return "Unknown";
   }

   private static string DetermineInterfaceType(CoreDriveInfo drive)
   {
      var path = drive.Path.ToLowerInvariant();
      if(path.Contains("nvme"))
         return "NVMe";
      if(drive.IsRemovable)
         return "USB";
      return "SATA";
   }

   private static double ApplySmartDeltaAfterSanitization(SmartaData smartBefore, SmartaData smartAfter, List<string> findings)
   {
        var penalty = 0d;

        var reallocatedDelta = (smartAfter.ReallocatedSectorCount ?? 0) - (smartBefore.ReallocatedSectorCount ?? 0);
        if (reallocatedDelta > 0)
        {
            findings.Add($"Po kompletním přepisu narostl počet realokovaných sektorů o {reallocatedDelta}. To je silný signál degradace povrchu.");
            penalty += Math.Min(28d, reallocatedDelta * 6d);
        }

        var pendingDelta = (smartAfter.PendingSectorCount ?? 0) - (smartBefore.PendingSectorCount ?? 0);
        if (pendingDelta > 0)
        {
            findings.Add($"Po testu narostl počet pending sektorů o {pendingDelta}. Disk může mít nestabilní nebo obtížně čitelné oblasti.");
            penalty += Math.Min(24d, pendingDelta * 5d);
        }

        var uncorrectableDelta = (smartAfter.UncorrectableErrorCount ?? 0) - (smartBefore.UncorrectableErrorCount ?? 0);
        if (uncorrectableDelta > 0)
        {
            findings.Add($"Po testu narostl počet neopravených chyb o {uncorrectableDelta}. To ukazuje na vážný problém média.");
            penalty += Math.Min(26d, uncorrectableDelta * 6d);
        }

        return penalty;
    }

    private static double ApplySanitizationDiagnostics(SanitizationResult result, List<string> findings, bool driveLooksSolidState)
    {
        var penalty = 0d;

        var startupRetries = result.ErrorDetails.Count(e => string.Equals(e.Phase, "StartupHandshake", StringComparison.Ordinal));
        if (startupRetries > 0)
        {
            findings.Add($"Start testu vyžadoval {startupRetries} retry handshake po odpojení svazků. To může ukazovat na pomalou re-enumeraci zařízení ve Windows.");
            penalty += Math.Min(12d, startupRetries * 2d);
        }

        var usbRetries = result.ErrorDetails.Count(e => string.Equals(e.ErrorCode, "USB_COMM_RETRY", StringComparison.Ordinal));
        if (usbRetries > 0)
        {
            findings.Add($"Během testu byly zachyceny výpadky USB komunikace ({usbRetries}x). To může ukazovat na nestabilní USB bridge, kabel nebo napájení.");
            penalty += Math.Min(14d, usbRetries * 2.5d);
        }

        if (driveLooksSolidState && usbRetries > 0)
        {
            findings.Add("U SSD přes USB je část nestability pravděpodobně ovlivněna bridge vrstvou, nejen médiem.");
        }

        return penalty;
    }

    private static double ApplySsdCacheCollapseAnalysis(IReadOnlyList<SpeedSample> writeSamples, List<string> findings)
    {
        if (writeSamples.Count < 10)
        {
            return 0d;
        }

        var ordered = writeSamples
            .Where(s => s.SpeedMBps > 0)
            .OrderBy(s => s.ProgressPercent)
            .ToList();
        if (ordered.Count < 10)
        {
            return 0d;
        }

        var head = ordered.Take(Math.Max(4, ordered.Count / 5)).ToList();
        var tail = ordered.Skip(Math.Max(0, ordered.Count - Math.Max(4, ordered.Count / 4))).ToList();
        var headAvg = head.Average(s => s.SpeedMBps);
        var tailAvg = tail.Average(s => s.SpeedMBps);
        if (headAvg <= 0 || tailAvg <= 0)
        {
            return 0d;
        }

        var dropRatio = 1d - (tailAvg / headAvg);
        if (dropRatio < 0.45d)
        {
            return 0d;
        }

        findings.Add($"Pravděpodobný SSD cache collapse: dlouhodobý pokles zápisu z {headAvg:F1} na {tailAvg:F1} MB/s ({dropRatio:P0}).");
        return Math.Clamp((dropRatio - 0.45d) * 28d, 0d, 14d);
    }
}