using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Application.Services;

public class HistoryService
{
    private readonly DiskCheckerDbContext _context;

    public HistoryService(DiskCheckerDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<TestHistoryItem>> GetHistoryAsync(
        int pageSize = 20, 
        int pageIndex = 0, 
        string? driveSerial = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        QualityGrade? gradeFilter = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Tests
            .Include(t => t.Drive)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(driveSerial))
        {
            query = query.Where(t => t.Drive.SerialNumber.Contains(driveSerial));
        }

        if (fromDate.HasValue)
        {
            query = query.Where(t => t.TestDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(t => t.TestDate <= toDate.Value);
        }

        if (gradeFilter.HasValue)
        {
            query = query.Where(t => t.Grade == gradeFilter.Value);
        }

        query = query.OrderByDescending(t => t.TestDate);

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(t => new TestHistoryItem
            {
                TestId = t.Id,
                DriveId = t.DriveId,
                DriveName = t.Drive.Name,
                DrivePath = t.Drive.Path,
                SerialNumber = t.Drive.SerialNumber,
                TestDate = t.TestDate,
                TestType = t.TestType,
                Grade = t.Grade,
                Score = t.Score,
                AverageSpeed = t.AverageSpeed,
                PeakSpeed = t.PeakSpeed,
                TotalBytesTested = t.TotalBytesTested,
                ErrorCount = t.Errors
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<TestHistoryItem>
        {
            Items = items,
            TotalItems = totalItems,
            PageSize = pageSize,
            PageIndex = pageIndex
        };
    }

    public async Task<List<TestHistoryItem>> GetForCompareAsync(int take = 5, CancellationToken cancellationToken = default)
    {
        return await _context.Tests
            .Include(t => t.Drive)
            .OrderByDescending(t => t.TestDate)
            .Take(take)
            .Select(t => new TestHistoryItem
            {
                TestId = t.Id,
                DriveId = t.DriveId,
                DriveName = t.Drive.Name,
                DrivePath = t.Drive.Path,
                SerialNumber = t.Drive.SerialNumber,
                TestDate = t.TestDate,
                TestType = t.TestType,
                Grade = t.Grade,
                Score = t.Score,
                AverageSpeed = t.AverageSpeed,
                PeakSpeed = t.PeakSpeed,
                TotalBytesTested = t.TotalBytesTested,
                ErrorCount = t.Errors
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<TestHistoryItem?> GetTestByIdAsync(Guid testId, CancellationToken cancellationToken = default)
    {
        var test = await _context.Tests
            .Include(t => t.Drive)
            .Include(t => t.SmartaData)
            .Include(t => t.SurfaceSamples)
            .FirstOrDefaultAsync(t => t.Id == testId, cancellationToken);

        if (test is null)
        {
            return null;
        }

        var testItem = new TestHistoryItem
        {
            TestId = test.Id,
            DriveId = test.DriveId,
            DriveName = test.Drive.Name,
            DrivePath = test.Drive.Path,
            SerialNumber = test.Drive.SerialNumber,
            TestDate = test.TestDate,
            TestType = test.TestType,
            Grade = test.Grade,
            Score = test.Score,
            AverageSpeed = test.AverageSpeed,
            PeakSpeed = test.PeakSpeed,
            MinSpeed = test.MinSpeed,
            TotalBytesTested = test.TotalBytesTested,
            ErrorCount = test.Errors,
            SmartaData = test.SmartaData != null ? new SmartaData
            {
                PowerOnHours = test.SmartaData.PowerOnHours,
                ReallocatedSectorCount = test.SmartaData.ReallocatedSectorCount,
                PendingSectorCount = test.SmartaData.PendingSectorCount,
                UncorrectableErrorCount = test.SmartaData.UncorrectableErrorCount,
                Temperature = test.SmartaData.Temperature,
                WearLevelingCount = test.SmartaData.WearLevelingCount
            } : null,
            SurfaceSamples = test.SurfaceSamples.Select(s => new SpeedSample
            {
                OffsetBytes = s.OffsetBytes,
                BlockSizeBytes = s.BlockSizeBytes,
                ThroughputMbps = s.ThroughputMbps,
                TimestampUtc = s.TimestampUtc,
                ErrorCount = s.ErrorCount
            }).ToList()
        };

        return testItem;
    }

    public async Task<List<CompareItem>> CompareTestsAsync(Guid testId1, Guid testId2, CancellationToken cancellationToken = default)
    {
        var test1 = await GetTestByIdAsync(testId1, cancellationToken);
        var test2 = await GetTestByIdAsync(testId2, cancellationToken);

        if (test1 is null || test2 is null)
        {
            throw new InvalidOperationException("Jeden z testů nebyl nalezen.");
        }

        return new List<CompareItem>
        {
            new CompareItem { Label = "Disk", Value1 = test1.DriveName, Value2 = test2.DriveName },
            new CompareItem { Label = "Sériové číslo", Value1 = test1.SerialNumber, Value2 = test2.SerialNumber },
            new CompareItem { Label = "Datum testu", Value1 = test1.TestDate.ToString("G"), Value2 = test2.TestDate.ToString("G") },
            new CompareItem { Label = "Typ testu", Value1 = test1.TestType, Value2 = test2.TestType },
            new CompareItem { Label = "Kvalita (známka)", Value1 = test1.Grade.ToString(), Value2 = test2.Grade.ToString() },
            new CompareItem { Label = "Skóre", Value1 = test1.Score.ToString("F1"), Value2 = test2.Score.ToString("F1") },
            new CompareItem { Label = "Průměrná rychlost (MB/s)", Value1 = test1.AverageSpeed.ToString("F1"), Value2 = test2.AverageSpeed.ToString("F1") },
            new CompareItem { Label = "Maximální rychlost (MB/s)", Value1 = test1.PeakSpeed.ToString("F1"), Value2 = test2.PeakSpeed.ToString("F1") },
            new CompareItem { Label = "Minimální rychlost (MB/s)", Value1 = test1.MinSpeed.ToString("F1"), Value2 = test2.MinSpeed.ToString("F1") },
            new CompareItem { Label = "Zaprotokolováno (B)", Value1 = test1.TotalBytesTested.ToString("N0"), Value2 = test2.TotalBytesTested.ToString("N0") },
            new CompareItem { Label = "Chyby", Value1 = test1.ErrorCount.ToString(), Value2 = test2.ErrorCount.ToString() }
        };
    }

    public async Task<List<DriveCompareItem>> GetDrivesWithTestsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Drives
            .OrderBy(d => d.Name)
            .Select(d => new DriveCompareItem
            {
                DriveId = d.Id,
                DriveName = d.Name,
                SerialNumber = d.SerialNumber,
                Model = d.DeviceModel,
                TotalTests = d.TotalTests,
                LastTestDate = d.Tests.Max(t => t.TestDate)
            })
            .ToListAsync(cancellationToken);
    }
}
