using System.IO.Compression;
using System.Text.Json;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Application.Services;

/// <summary>
/// Exports and imports per-drive history archives.
/// </summary>
public class DiskHistoryArchiveService
{
    private const string ArchiveEntryName = "disk-history.json";
    private readonly DiskCheckerDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskHistoryArchiveService"/> class.
    /// </summary>
    /// <param name="dbContext">Database context.</param>
    public DiskHistoryArchiveService(DiskCheckerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Archives all tests for the selected drive except the latest one and removes archived tests from DB.
    /// </summary>
    /// <param name="driveId">Drive identifier.</param>
    /// <param name="zipPath">Target zip file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of archived tests.</returns>
    public async Task<int> ArchiveDriveHistoryAsync(Guid driveId, string zipPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new ArgumentException("Zip path must not be empty.", nameof(zipPath));
        }

        var drive = await _dbContext.Drives
            .Include(d => d.Tests)
                .ThenInclude(t => t.SmartaData)
            .Include(d => d.Tests)
                .ThenInclude(t => t.SurfaceSamples)
            .SingleOrDefaultAsync(d => d.Id == driveId, cancellationToken);

        if (drive == null)
        {
            throw new InvalidOperationException("Drive was not found.");
        }

        var orderedTests = drive.Tests.OrderByDescending(t => t.TestDate).ToList();
        if (orderedTests.Count <= 1)
        {
            return 0;
        }

        var testsToArchive = orderedTests.Skip(1).ToList();
        var payload = new DriveArchivePayload
        {
            Drive = new ArchiveDrive
            {
                IdentityKey = drive.SerialNumber,
                Name = drive.Name,
                Path = drive.Path,
                DeviceModel = drive.DeviceModel,
                ModelFamily = drive.ModelFamily,
                FirmwareVersion = drive.FirmwareVersion,
                FileSystem = drive.FileSystem,
                TotalSize = drive.TotalSize,
                Tests = testsToArchive.Select(ToArchiveTest).ToList()
            }
        };

        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        await using (var fileStream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry(ArchiveEntryName, CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await JsonSerializer.SerializeAsync(entryStream, payload, cancellationToken: cancellationToken);
        }

        var testIds = testsToArchive.Select(t => t.Id).ToList();
        var smartRows = await _dbContext.SmartaData.Where(s => testIds.Contains(s.TestId)).ToListAsync(cancellationToken);
        var sampleRows = await _dbContext.SurfaceTestSamples.Where(s => testIds.Contains(s.TestId)).ToListAsync(cancellationToken);

        _dbContext.SmartaData.RemoveRange(smartRows);
        _dbContext.SurfaceTestSamples.RemoveRange(sampleRows);
        _dbContext.Tests.RemoveRange(testsToArchive);

        drive.TotalTests = Math.Max(1, drive.TotalTests - testsToArchive.Count);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return testsToArchive.Count;
    }

    /// <summary>
    /// Imports archived drive history from ZIP and merges records into database.
    /// </summary>
    /// <param name="zipPath">Source zip path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of imported tests.</returns>
    public async Task<int> ImportDriveHistoryArchiveAsync(string zipPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new ArgumentException("Zip path must not be empty.", nameof(zipPath));
        }

        if (!File.Exists(zipPath))
        {
            throw new InvalidOperationException("Archive file was not found.");
        }

        DriveArchivePayload payload;
        await using (var fileStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Read))
        {
            var entry = zip.GetEntry(ArchiveEntryName)
                ?? throw new InvalidOperationException("Archive format is invalid.");

            await using var entryStream = entry.Open();
            payload = await JsonSerializer.DeserializeAsync<DriveArchivePayload>(entryStream, cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Archive payload is empty.");
        }

        if (payload.Drive == null)
        {
            throw new InvalidOperationException("Archive drive payload is missing.");
        }

        var drive = await _dbContext.Drives
            .SingleOrDefaultAsync(d => d.SerialNumber == payload.Drive.IdentityKey, cancellationToken);

        if (drive == null)
        {
            drive = new DriveRecord
            {
                Id = Guid.NewGuid(),
                SerialNumber = payload.Drive.IdentityKey,
                Name = payload.Drive.Name,
                Path = payload.Drive.Path,
                DeviceModel = payload.Drive.DeviceModel,
                ModelFamily = payload.Drive.ModelFamily,
                FirmwareVersion = payload.Drive.FirmwareVersion,
                FileSystem = payload.Drive.FileSystem,
                TotalSize = payload.Drive.TotalSize,
                FirstSeen = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow,
                TotalTests = 0
            };
            _dbContext.Drives.Add(drive);
        }

        var imported = 0;
        foreach (var archivedTest in payload.Drive.Tests)
        {
            var exists = await _dbContext.Tests.AnyAsync(t =>
                t.DriveId == drive.Id
                && t.TestDate == archivedTest.TestDate
                && t.TestType == archivedTest.TestType
                && t.Score == archivedTest.Score,
                cancellationToken);

            if (exists)
            {
                continue;
            }

            var testRecord = new TestRecord
            {
                Id = Guid.NewGuid(),
                DriveId = drive.Id,
                TestDate = archivedTest.TestDate,
                TestType = archivedTest.TestType,
                AverageSpeed = archivedTest.AverageSpeed,
                PeakSpeed = archivedTest.PeakSpeed,
                MinSpeed = archivedTest.MinSpeed,
                TotalBytesWritten = archivedTest.TotalBytesWritten,
                TotalBytesTested = archivedTest.TotalBytesTested,
                Errors = archivedTest.Errors,
                Grade = archivedTest.Grade,
                Score = archivedTest.Score,
                SurfaceProfile = archivedTest.SurfaceProfile,
                SurfaceOperation = archivedTest.SurfaceOperation,
                SurfaceTechnology = archivedTest.SurfaceTechnology,
                SecureErasePerformed = archivedTest.SecureErasePerformed,
                IsCompleted = archivedTest.IsCompleted,
                IsArchived = false
            };

            if (archivedTest.SmartaData != null)
            {
                testRecord.SmartaData = new SmartaRecord
                {
                    Id = Guid.NewGuid(),
                    TestId = testRecord.Id,
                    PowerOnHours = archivedTest.SmartaData.PowerOnHours,
                    ReallocatedSectorCount = archivedTest.SmartaData.ReallocatedSectorCount,
                    PendingSectorCount = archivedTest.SmartaData.PendingSectorCount,
                    UncorrectableErrorCount = archivedTest.SmartaData.UncorrectableErrorCount,
                    Temperature = archivedTest.SmartaData.Temperature,
                    WearLevelingCount = archivedTest.SmartaData.WearLevelingCount,
                    Test = testRecord
                };
            }

            foreach (var s in archivedTest.Samples)
            {
                testRecord.SurfaceSamples.Add(new SurfaceTestSampleRecord
                {
                    Id = Guid.NewGuid(),
                    TestId = testRecord.Id,
                    OffsetBytes = s.OffsetBytes,
                    BlockSizeBytes = s.BlockSizeBytes,
                    ThroughputMbps = s.ThroughputMbps,
                    TimestampUtc = s.TimestampUtc,
                    ErrorCount = s.ErrorCount,
                    Test = testRecord
                });
            }

            _dbContext.Tests.Add(testRecord);
            imported++;
        }

        drive.LastSeen = DateTime.UtcNow;
        drive.TotalTests += imported;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return imported;
    }

    private static ArchiveTest ToArchiveTest(TestRecord test)
    {
        return new ArchiveTest
        {
            TestDate = test.TestDate,
            TestType = test.TestType,
            AverageSpeed = test.AverageSpeed,
            PeakSpeed = test.PeakSpeed,
            MinSpeed = test.MinSpeed,
            TotalBytesWritten = test.TotalBytesWritten,
            TotalBytesTested = test.TotalBytesTested,
            Errors = test.Errors,
            Grade = test.Grade,
            Score = test.Score,
            SurfaceProfile = test.SurfaceProfile,
            SurfaceOperation = test.SurfaceOperation,
            SurfaceTechnology = test.SurfaceTechnology,
            SecureErasePerformed = test.SecureErasePerformed,
            IsCompleted = test.IsCompleted,
            SmartaData = test.SmartaData == null
                ? null
                : new ArchiveSmartaData
                {
                    PowerOnHours = test.SmartaData.PowerOnHours,
                    ReallocatedSectorCount = test.SmartaData.ReallocatedSectorCount,
                    PendingSectorCount = test.SmartaData.PendingSectorCount,
                    UncorrectableErrorCount = test.SmartaData.UncorrectableErrorCount,
                    Temperature = test.SmartaData.Temperature,
                    WearLevelingCount = test.SmartaData.WearLevelingCount
                },
            Samples = test.SurfaceSamples
                .OrderBy(s => s.TimestampUtc)
                .Select(s => new ArchiveSpeedSample
                {
                    OffsetBytes = s.OffsetBytes,
                    BlockSizeBytes = s.BlockSizeBytes,
                    ThroughputMbps = s.ThroughputMbps,
                    TimestampUtc = s.TimestampUtc,
                    ErrorCount = s.ErrorCount
                })
                .ToList()
        };
    }

    private sealed class DriveArchivePayload
    {
        public ArchiveDrive? Drive { get; set; }
    }

    private sealed class ArchiveDrive
    {
        public string IdentityKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public string ModelFamily { get; set; } = string.Empty;
        public string FirmwareVersion { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public List<ArchiveTest> Tests { get; set; } = [];
    }

    private sealed class ArchiveTest
    {
        public DateTime TestDate { get; set; }
        public string TestType { get; set; } = string.Empty;
        public double AverageSpeed { get; set; }
        public double PeakSpeed { get; set; }
        public double MinSpeed { get; set; }
        public long TotalBytesWritten { get; set; }
        public long TotalBytesTested { get; set; }
        public int Errors { get; set; }
        public QualityGrade Grade { get; set; }
        public double Score { get; set; }
        public SurfaceTestProfile? SurfaceProfile { get; set; }
        public SurfaceTestOperation? SurfaceOperation { get; set; }
        public DriveTechnology? SurfaceTechnology { get; set; }
        public bool? SecureErasePerformed { get; set; }
        public bool IsCompleted { get; set; }
        public ArchiveSmartaData? SmartaData { get; set; }
        public List<ArchiveSpeedSample> Samples { get; set; } = [];
    }

    private sealed class ArchiveSmartaData
    {
        public int PowerOnHours { get; set; }
        public long ReallocatedSectorCount { get; set; }
        public long PendingSectorCount { get; set; }
        public long UncorrectableErrorCount { get; set; }
        public double Temperature { get; set; }
        public int? WearLevelingCount { get; set; }
    }

    private sealed class ArchiveSpeedSample
    {
        public long OffsetBytes { get; set; }
        public int BlockSizeBytes { get; set; }
        public double ThroughputMbps { get; set; }
        public DateTime TimestampUtc { get; set; }
        public int ErrorCount { get; set; }
    }
}
