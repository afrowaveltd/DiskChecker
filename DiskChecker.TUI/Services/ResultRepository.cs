using DiskChecker.TUI.Data;
using DiskChecker.TUI.Models;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Persists test results to SQLite database.
/// </summary>
public sealed class ResultRepository
{
    private readonly string _dbPath;

    public ResultRepository(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// Ensures the database is created.
    /// </summary>
    public async Task InitializeAsync()
    {
        using var db = new TuiDbContext(_dbPath);
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Saves a test run result to the database.
    /// </summary>
    public async Task SaveResultAsync(TestRunResult result)
    {
        using var db = new TuiDbContext(_dbPath);

        // Find or create disk record
        var diskRecord = await db.Disks
            .FirstOrDefaultAsync(d => d.SerialNumber == result.DiskSerial && d.DevicePath == result.DevicePath);

        if (diskRecord == null)
        {
            diskRecord = new TuiDiskRecord
            {
                Model = result.DiskModel,
                SerialNumber = result.DiskSerial,
                DevicePath = result.DevicePath,
                CapacityBytes = result.CapacityBytes,
                FirstSeen = result.StartedAt,
                LastTested = result.CompletedAt
            };
            db.Disks.Add(diskRecord);
            await db.SaveChangesAsync();
        }
        else
        {
            diskRecord.LastTested = result.CompletedAt;
            diskRecord.Model = result.DiskModel; // Update in case firmware changed
        }

        // Create test session
        var session = new TuiTestSession
        {
            DiskRecordId = diskRecord.Id,
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
            TestType = "FullDestructive",
            WriteSpeedAvgMBps = result.WriteSpeedAvgMBps,
            WriteSpeedMinMBps = result.WriteSpeedMinMBps,
            WriteSpeedMaxMBps = result.WriteSpeedMaxMBps,
            ReadSpeedAvgMBps = result.ReadSpeedAvgMBps,
            ReadSpeedMinMBps = result.ReadSpeedMinMBps,
            ReadSpeedMaxMBps = result.ReadSpeedMaxMBps,
            SeekAvgMs = result.SeekAvgMs,
            SeekMinMs = result.SeekMinMs,
            SeekMaxMs = result.SeekMaxMs,
            MaxTemperatureC = result.MaxTemperatureC,
            AvgTemperatureC = result.AvgTemperatureC,
            SanitizationPassed = result.SanitizationPassed,
            SanitizationMethod = result.SanitizationMethod,
            SanitizationOutput = result.SanitizationOutput,
            ErrorMessage = result.ErrorMessage,
            Grade = result.Grade
        };

        db.TestSessions.Add(session);
        await db.SaveChangesAsync();

        // Save write samples
        foreach (var sample in result.WriteSamples)
        {
            db.SpeedSamples.Add(new TuiSpeedSample
            {
                TestSessionId = session.Id,
                IsWrite = true,
                PositionPercent = sample.PositionPercent,
                SpeedMBps = sample.SpeedMBps,
                Timestamp = sample.Timestamp
            });
        }

        // Save read samples
        foreach (var sample in result.ReadSamples)
        {
            db.SpeedSamples.Add(new TuiSpeedSample
            {
                TestSessionId = session.Id,
                IsWrite = false,
                PositionPercent = sample.PositionPercent,
                SpeedMBps = sample.SpeedMBps,
                Timestamp = sample.Timestamp
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Gets recent test history for display.
    /// </summary>
    public async Task<List<TuiTestSession>> GetRecentSessionsAsync(int count = 10)
    {
        using var db = new TuiDbContext(_dbPath);
        return await db.TestSessions
            .OrderByDescending(s => s.CompletedAt)
            .Take(count)
            .ToListAsync();
    }

    /// <summary>
    /// Gets disk records with their test counts.
    /// </summary>
    public async Task<List<TuiDiskRecord>> GetDisksAsync()
    {
        using var db = new TuiDbContext(_dbPath);
        return await db.Disks
            .OrderByDescending(d => d.LastTested)
            .ToListAsync();
    }
}
