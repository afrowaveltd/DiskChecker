using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Application.Services;

/// <summary>
/// Service for managing test history and reports.
/// </summary>
public class HistoryService
{
    private readonly DiskCheckerDbContext _dbContext;

    public HistoryService(DiskCheckerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Get all test reports.
    /// </summary>
    public async Task<IEnumerable<TestReport>> GetReportsAsync(CancellationToken cancellationToken = default)
    {
        var tests = await _dbContext.Tests
            .Include(t => t.Drive)
            .OrderByDescending(t => t.TestDate)
            .Take(100)
            .ToListAsync(cancellationToken);

        return tests.Select(t => new TestReport
        {
            ReportId = t.Id,
            TestDate = t.TestDate,
            TestType = t.TestType,
            Grade = t.Grade ?? "F",
            Score = t.Score,
            DriveModel = t.Drive?.DeviceModel ?? "Unknown",
            SerialNumber = t.Drive?.SerialNumber ?? "Unknown",
            AverageSpeed = t.AverageSpeed,
            PeakSpeed = t.PeakSpeed,
            Errors = t.Errors,
            IsCompleted = t.IsCompleted
        });
    }

    /// <summary>
    /// Generate a new report.
    /// </summary>
    public async Task<TestReport> GenerateReportAsync(CancellationToken cancellationToken = default)
    {
        var latestTest = await _dbContext.Tests
            .Include(t => t.Drive)
            .OrderByDescending(t => t.TestDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestTest == null)
        {
            return new TestReport
            {
                ReportId = Guid.NewGuid(),
                TestDate = DateTime.UtcNow,
                TestType = "None",
                Grade = "N/A",
                Score = 0,
                DriveModel = "No tests available",
                SerialNumber = "N/A"
            };
        }

        return new TestReport
        {
            ReportId = latestTest.Id,
            TestDate = latestTest.TestDate,
            TestType = latestTest.TestType,
            Grade = latestTest.Grade ?? "F",
            Score = latestTest.Score,
            DriveModel = latestTest.Drive?.DeviceModel ?? "Unknown",
            SerialNumber = latestTest.Drive?.SerialNumber ?? "Unknown",
            AverageSpeed = latestTest.AverageSpeed,
            PeakSpeed = latestTest.PeakSpeed,
            Errors = latestTest.Errors,
            IsCompleted = latestTest.IsCompleted
        };
    }

    /// <summary>
    /// Delete a report by ID.
    /// </summary>
    public async Task DeleteReportAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var test = await _dbContext.Tests.FindAsync(new object[] { reportId }, cancellationToken);
        if (test != null)
        {
            _dbContext.Tests.Remove(test);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Export a report to the specified format.
    /// </summary>
    public async Task ExportReportAsync(TestReport report, string format, CancellationToken cancellationToken = default)
    {
        // Placeholder for export logic
        await Task.Delay(100, cancellationToken);
        
        var test = await _dbContext.Tests.FindAsync(new object[] { report.ReportId }, cancellationToken);
        if (test != null)
        {
            test.IsCompleted = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Get history for a specific disk by serial number.
    /// </summary>
    public async Task<IEnumerable<HistoricalTest>> GetHistoryForDiskAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        var tests = await _dbContext.Tests
            .Include(t => t.Drive)
            .Where(t => t.Drive != null && t.Drive.SerialNumber == serialNumber)
            .OrderByDescending(t => t.TestDate)
            .ToListAsync(cancellationToken);

        return tests.Select(t => new HistoricalTest
        {
            Id = t.Id,
            SerialNumber = t.Drive?.SerialNumber,
            Model = t.Drive?.DeviceModel,
            TestDate = t.TestDate,
            TestType = t.TestType,
            Grade = t.Grade ?? "F",
            Score = t.Score,
            ErrorCount = t.Errors,
            AverageThroughputMbps = t.AverageSpeed,
            PeakThroughputMbps = t.PeakSpeed,
            TotalBytesTested = t.TotalBytesTested
        });
    }

    /// <summary>
    /// Get all test history.
    /// </summary>
    public async Task<IEnumerable<HistoricalTest>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var tests = await _dbContext.Tests
            .Include(t => t.Drive)
            .OrderByDescending(t => t.TestDate)
            .Take(100)
            .ToListAsync(cancellationToken);

        return tests.Select(t => new HistoricalTest
        {
            Id = t.Id,
            SerialNumber = t.Drive?.SerialNumber,
            Model = t.Drive?.DeviceModel,
            TestDate = t.TestDate,
            TestType = t.TestType,
            Grade = t.Grade ?? "F",
            Score = t.Score,
            ErrorCount = t.Errors,
            AverageThroughputMbps = t.AverageSpeed,
            PeakThroughputMbps = t.PeakSpeed,
            TotalBytesTested = t.TotalBytesTested
        });
    }

    /// <summary>
    /// Delete a history record.
    /// </summary>
    public async Task DeleteHistoryAsync(Guid testId, CancellationToken cancellationToken = default)
    {
        var test = await _dbContext.Tests.FindAsync(new object[] { testId }, cancellationToken);
        if (test != null)
        {
            _dbContext.Tests.Remove(test);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Clear all history.
    /// </summary>
    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        var allTests = await _dbContext.Tests.ToListAsync(cancellationToken);
        _dbContext.Tests.RemoveRange(allTests);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}