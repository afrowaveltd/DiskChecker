using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Application.Services;

/// <summary>
/// Persists surface test results into the database.
/// </summary>
public class SurfaceTestPersistenceService
{
   private const string SurfaceTestType = "SurfaceTest";
   private readonly DiskCheckerDbContext _dbContext;

   /// <summary>
   /// Initializes a new instance of the <see cref="SurfaceTestPersistenceService"/> class.
   /// </summary>
   /// <param name="dbContext">Database context for persistence.</param>
   public SurfaceTestPersistenceService(DiskCheckerDbContext dbContext)
   {
      _dbContext = dbContext;
   }

   /// <summary>
   /// Saves the provided surface test result.
   /// </summary>
   /// <param name="result">Surface test result to persist.</param>
   /// <param name="drive">Drive information from the request.</param>
   /// <param name="cancellationToken">Token to cancel the operation.</param>
   /// <returns>The persisted test identifier.</returns>
   public async Task<Guid> SaveAsync(SurfaceTestResult result, CoreDriveInfo drive, CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(result);
      ArgumentNullException.ThrowIfNull(drive);

      var testDate = result.CompletedAtUtc == default ? DateTime.UtcNow : result.CompletedAtUtc;
      var serialKey = DriveIdentityResolver.BuildIdentityKey(
          drive.Path,
          result.DriveSerialNumber,
          result.DriveModel ?? drive.Name,
          null);

      var driveRecord = await _dbContext.Drives
          .SingleOrDefaultAsync(d => d.SerialNumber == serialKey, cancellationToken);

      if(driveRecord == null)
      {
         driveRecord = new DriveRecord
         {
            Id = Guid.NewGuid(),
            Path = drive.Path,
            Name = result.DriveModel ?? drive.Name,
            SerialNumber = serialKey,
            FirmwareVersion = string.Empty,
            FileSystem = drive.FileSystem,
            TotalSize = result.DriveTotalBytes > 0 ? result.DriveTotalBytes : drive.TotalSize,
            FreeSpace = drive.FreeSpace,
            FirstSeen = testDate,
            LastSeen = testDate,
            TotalTests = 0
         };

         _dbContext.Drives.Add(driveRecord);
      }
      else
      {
         driveRecord.Path = drive.Path;
         driveRecord.Name = result.DriveModel ?? drive.Name;
         driveRecord.FileSystem = drive.FileSystem;
         driveRecord.TotalSize = result.DriveTotalBytes > 0 ? result.DriveTotalBytes : drive.TotalSize;
         driveRecord.FreeSpace = drive.FreeSpace;
         driveRecord.LastSeen = testDate;
      }

      driveRecord.TotalTests += 1;

      // Calculate test rating based on results
      var grade = CalculateSurfaceTestGrade(result);
      var score = CalculateSurfaceTestScore(result);

      var testRecord = new TestRecord
      {
         Id = Guid.NewGuid(),
         DriveId = driveRecord.Id,
         TestDate = testDate,
         TestType = SurfaceTestType,
         AverageSpeed = result.AverageSpeedMbps,
         PeakSpeed = result.PeakSpeedMbps,
         MinSpeed = result.MinSpeedMbps,
         TotalBytesWritten = result.Operation == SurfaceTestOperation.ReadOnly ? 0 : result.TotalBytesTested,
         TotalBytesTested = result.TotalBytesTested,
         Errors = result.ErrorCount,
          Grade = grade.ToString(),
          Score = (int)score,
          CertificatePath = string.Empty,
          SurfaceProfile = result.Profile.ToString(),
          SurfaceOperation = result.Operation.ToString(),
          SurfaceTechnology = DriveTechnology.Unknown.ToString(),
         SecureErasePerformed = result.SecureErasePerformed,
         IsCompleted = result.CompletedAtUtc != default && result.ErrorCount >= 0
      };

      foreach(var sample in result.Samples)
      {
         testRecord.SurfaceSamples.Add(new SurfaceTestSampleRecord
         {
            Id = Guid.NewGuid(),
            TestId = testRecord.Id,
            OffsetBytes = sample.OffsetBytes,
            BlockSizeBytes = sample.BlockSizeBytes,
            ThroughputMbps = sample.ThroughputMbps,
            TimestampUtc = sample.TimestampUtc,
            ErrorCount = sample.ErrorCount,
            Test = testRecord
         });
      }

      _dbContext.Tests.Add(testRecord);
      await _dbContext.SaveChangesAsync(cancellationToken);

      return testRecord.Id;
   }

   /// <summary>
   /// Calculates quality grade for surface test based on error count and completion.
   /// </summary>
   private static QualityGrade CalculateSurfaceTestGrade(SurfaceTestResult result)
   {
      // Critical failures
      if(result.ErrorCount > 100)
         return QualityGrade.F;

      if(result.ErrorCount > 10)
         return QualityGrade.E;

      if(result.ErrorCount > 5)
         return QualityGrade.D;

      if(result.ErrorCount > 0)
         return QualityGrade.C;

      // No errors - check performance
      if(result.AverageSpeedMbps > 100)
         return QualityGrade.A;

      if(result.AverageSpeedMbps > 50)
         return QualityGrade.B;

      // Slow but no errors
      return QualityGrade.C;
   }

   /// <summary>
   /// Calculates numeric score for surface test (0-100).
   /// </summary>
   private static double CalculateSurfaceTestScore(SurfaceTestResult result)
   {
      double score = 100.0;

      // Penalty for errors (severe)
      if(result.ErrorCount > 0)
      {
         var errorPenalty = Math.Min(90, result.ErrorCount * 10);  // Up to -90 points
         score -= errorPenalty;
      }

      // Bonus for good speed (minor)
      if(result.AverageSpeedMbps > 100)
         score = Math.Min(100, score + 10);  // +10 for excellent speed
      else if(result.AverageSpeedMbps < 30)
         score -= 10;  // -10 for very slow

      // Ensure 0-100 range
      return Math.Max(0, Math.Min(100, score));
   }
}
