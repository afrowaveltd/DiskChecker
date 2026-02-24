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
        var serialKey = result.DriveSerialNumber ?? (string.IsNullOrWhiteSpace(drive.Path) ? drive.Name : drive.Path);

        var driveRecord = await _dbContext.Drives
            .SingleOrDefaultAsync(d => d.SerialNumber == serialKey, cancellationToken);

        if (driveRecord == null)
        {
            driveRecord = new DriveRecord
            {
                Id = Guid.NewGuid(),
                Path = drive.Path,
                Name = result.DriveModel ?? drive.Name,
                SerialNumber = serialKey,
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
            Grade = QualityGrade.C,
            Score = 0,
            CertificatePath = string.Empty,
            SurfaceProfile = result.Profile,
            SurfaceOperation = result.Operation,
            SurfaceTechnology = DriveTechnology.Unknown,
            SecureErasePerformed = result.SecureErasePerformed
        };

        foreach (var sample in result.Samples)
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
}
