using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Application.Services;

/// <summary>
/// Represents cleanup impact preview for maintenance operations.
/// </summary>
public class DatabaseCleanupPreview
{
    /// <summary>
    /// Gets or sets number of invalid or incomplete tests.
    /// </summary>
    public int InvalidTests { get; set; }

    /// <summary>
    /// Gets or sets number of duplicated tests.
    /// </summary>
    public int DuplicateTests { get; set; }

    /// <summary>
    /// Gets total number of rows that would be removed.
    /// </summary>
    public int TotalToRemove => InvalidTests + DuplicateTests;
}

/// <summary>
/// Provides maintenance operations for DiskChecker database.
/// </summary>
public class DatabaseMaintenanceService
{
    private readonly DiskCheckerDbContext _dbContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseMaintenanceService"/> class.
    /// </summary>
    /// <param name="dbContext">Database context.</param>
    public DatabaseMaintenanceService(DiskCheckerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Deletes invalid or incomplete tests from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of removed tests.</returns>
    public async Task<int> DeleteInvalidAndIncompleteTestsAsync(CancellationToken cancellationToken = default)
    {
        var invalidTests = await _dbContext.Tests
            .Where(t => !t.IsCompleted || t.TestDate == default || string.IsNullOrWhiteSpace(t.TestType))
            .ToListAsync(cancellationToken);

        if (invalidTests.Count == 0)
        {
            return 0;
        }

        var ids = invalidTests.Select(t => t.Id).ToList();
        var smartRows = await _dbContext.SmartaData.Where(s => ids.Contains(s.TestId)).ToListAsync(cancellationToken);
        var sampleRows = await _dbContext.SurfaceTestSamples.Where(s => ids.Contains(s.TestId)).ToListAsync(cancellationToken);

        _dbContext.SmartaData.RemoveRange(smartRows);
        _dbContext.SurfaceTestSamples.RemoveRange(sampleRows);
        _dbContext.Tests.RemoveRange(invalidTests);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return invalidTests.Count;
    }

    /// <summary>
    /// Removes duplicated tests for the same drive and timestamp while preserving the newest row.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of removed duplicated rows.</returns>
    public async Task<int> RemoveDuplicateTestsAsync(CancellationToken cancellationToken = default)
    {
        var allTests = await _dbContext.Tests
            .OrderByDescending(t => t.TestDate)
            .ToListAsync(cancellationToken);

        var duplicates = allTests
            .GroupBy(t => new { t.DriveId, t.TestType, t.TestDate })
            .SelectMany(g => g.Skip(1))
            .ToList();

        if (duplicates.Count == 0)
        {
            return 0;
        }

        var ids = duplicates.Select(t => t.Id).ToList();
        var smartRows = await _dbContext.SmartaData.Where(s => ids.Contains(s.TestId)).ToListAsync(cancellationToken);
        var sampleRows = await _dbContext.SurfaceTestSamples.Where(s => ids.Contains(s.TestId)).ToListAsync(cancellationToken);

        _dbContext.SmartaData.RemoveRange(smartRows);
        _dbContext.SurfaceTestSamples.RemoveRange(sampleRows);
        _dbContext.Tests.RemoveRange(duplicates);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return duplicates.Count;
    }

    /// <summary>
    /// Calculates cleanup impact without deleting data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Preview of rows that would be removed.</returns>
    public async Task<DatabaseCleanupPreview> PreviewCleanupAsync(CancellationToken cancellationToken = default)
    {
        var invalidCount = await _dbContext.Tests
            .CountAsync(t => !t.IsCompleted || t.TestDate == default || string.IsNullOrWhiteSpace(t.TestType), cancellationToken);

        var duplicateCount = await _dbContext.Tests
            .GroupBy(t => new { t.DriveId, t.TestType, t.TestDate })
            .Select(g => Math.Max(0, g.Count() - 1))
            .SumAsync(cancellationToken);

        return new DatabaseCleanupPreview
        {
            InvalidTests = invalidCount,
            DuplicateTests = duplicateCount
        };
    }
}
