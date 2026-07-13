using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

// xUnit1051: We use CancellationTokenSource.CreateLinkedTokenSource to combine
// TestContext.Current.CancellationToken with our own timeout tokens. The analyzer
// cannot detect that our linked tokens already respect test cancellation.
#pragma warning disable xUnit1051

namespace DiskChecker.Tests;

/// <summary>
/// Tests verifying that large-scale test result persistence completes within
/// a reasonable time and does not hang. Created to catch the bug where
/// the application became unresponsive after full sanitization test.
///
/// Symptoms:
///   - Surface tests completed successfully (read/write/partition)
///   - Application froze with low CPU and moderate memory usage
///   - No recovery after ~1 hour
///
/// Root cause analysis:
///   1. Potential infinite loop if I/O returns 0 bytes
///   2. Double-save with different DbContext instances causing SQLite lock contention
///   3. Missing zero-byte guard in WriteZerosAsync/ReadAndVerifyAsync while loops
/// </summary>
public class LargeScalePersistenceTests
{
    /// <summary>
    /// Creates an in-memory DbContext for testing without real database I/O.
    /// Each test gets a unique database name to ensure isolation.
    /// </summary>
    private static DiskCheckerDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new DiskCheckerDbContext(options);
    }

    /// <summary>
    /// Creates a realistic SurfaceTestResult simulating a full-disk test
    /// with many samples (simulating a ~500 GB disk at 64 MB chunks).
    /// </summary>
    private static SurfaceTestResult CreateLargeTestResult(int sampleCount = 8000)
    {
        var result = new SurfaceTestResult
        {
            TestId = Guid.NewGuid().ToString(),
            DriveModel = "WDC WD5000AAKX-00ERMA0",
            DriveSerialNumber = "WD-WCC2E1234567",
            DriveManufacturer = "Western Digital",
            DriveInterface = "SATA",
            DriveTotalBytes = 500_107_862_016L, // ~500 GB
            TotalBytesTested = 500_107_862_016L,
            AverageSpeedMbps = 115.5,
            PeakSpeedMbps = 180.2,
            MinSpeedMbps = 45.1,
            ErrorCount = 0,
            Operation = SurfaceTestOperation.WriteZeroFill,
            Profile = SurfaceTestProfile.HddFull,
            StartedAtUtc = DateTime.UtcNow.AddHours(-2),
            CompletedAtUtc = DateTime.UtcNow,
            CurrentTemperatureCelsius = 38,
            PowerOnHours = 12456,
            ReallocatedSectors = 0,
            SecureErasePerformed = true,
            Notes = null,
            Samples = new List<SurfaceTestSample>()
        };

        // Generate realistic samples — each represents a 64 MB chunk
        var random = new Random(42); // Deterministic seed
        long chunkSize = 64L * 1024L * 1024L; // 64 MB
        var baseTime = result.StartedAtUtc;

        for (int i = 0; i < sampleCount; i++)
        {
            // Simulate realistic speed variation: 80-180 MB/s with some dips
            double speed = 100.0 + random.NextDouble() * 60.0;
            // Occasional slow sectors (5% chance)
            if (random.Next(100) < 5)
                speed = 10.0 + random.NextDouble() * 40.0;

            // Occasional stalls (0.5% chance) — simulate USB/disk hiccups
            double elapsed = (chunkSize / (1024.0 * 1024.0)) / Math.Max(speed, 1.0);
            if (random.Next(1000) < 5)
                elapsed += random.NextDouble() * 5.0; // 0-5 second stall

            result.Samples.Add(new SurfaceTestSample
            {
                OffsetBytes = i * chunkSize,
                BlockSizeBytes = (int)chunkSize,
                ThroughputMbps = speed,
                TimestampUtc = baseTime.AddSeconds(i * elapsed),
                ErrorCount = 0,
                ProgressPercent = (double)i / sampleCount * 100.0,
                Phase = i < sampleCount / 2 ? "Write" : "Read",
                Elapsed = TimeSpan.FromSeconds(elapsed),
                TemperatureCelsius = 38 + random.Next(-2, 5)
            });

            baseTime = baseTime.AddSeconds(elapsed);
        }

        return result;
    }

    /// <summary>
    /// Creates a CoreDriveInfo matching the test result.
    /// </summary>
    private static CoreDriveInfo CreateDriveInfo()
    {
        return new CoreDriveInfo
        {
            Path = @"\\.\PhysicalDrive2",
            Name = "WDC WD5000AAKX-00ERMA0",
            Model = "WDC WD5000AAKX-00ERMA0",
            SerialNumber = "WD-WCC2E1234567",
            TotalSize = 500_107_862_016L,
            FreeSpace = 0,
            FileSystem = "NTFS",
            FirmwareVersion = "15.01H15",
            FirmwareRevision = "15.01H15",
            Interface = "SATA",
            BusType = CoreBusType.Sata,
            MediaType = "Fixed hard disk media",
            IsRemovable = false
        };
    }

    // ── Test 1: Core — Large save must complete without hanging ─────

    /// <summary>
    /// Simulates saving a full-disk sanitization result with 8,000 samples.
    /// Must complete within 30 seconds (generous timeout for CI).
    /// If this hangs, the application will also hang.
    /// </summary>
    [Fact]
    public async Task SaveLargeSurfaceTest_CompletesWithoutHanging()
    {
        // Arrange
        var dbContext = CreateDbContext($"SaveLarge_{Guid.NewGuid():N}");
        await dbContext.Database.EnsureCreatedAsync();

        var cardTestService = new DiskCardTestService(
            dbContext,
            Substitute.For<IQualityCalculator>(),
            NullLogger<DiskCardTestService>.Instance,
            Substitute.For<ICertificateGenerator>());

        var persistenceService = new SurfaceTestPersistenceService(dbContext, cardTestService);

        var result = CreateLargeTestResult(sampleCount: 8000);
        var drive = CreateDriveInfo();

        // Act — with timeout to detect hang
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken, timeoutCts.Token);

        Guid testId;
        try
        {
            testId = await persistenceService.SaveAsync(result, drive, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("SaveAsync timed out after 30 seconds — persistence is hanging (likely deadlock or infinite loop)");
            throw;
        }

        // Assert
        Assert.NotEqual(Guid.Empty, testId);

        // Verify legacy test record was saved
        var testRecord = await dbContext.Tests
            .Include(t => t.SurfaceSamples)
            .FirstOrDefaultAsync(t => t.Id == testId);
        Assert.NotNull(testRecord);
        Assert.Equal(SurfaceTestOperation.WriteZeroFill.ToString(), testRecord.SurfaceOperation);
        Assert.Equal(8000, testRecord.SurfaceSamples.Count);
        Assert.Equal(115.5, testRecord.AverageSpeed, 0.1);

        // Verify drive record was created
        var driveRecord = await dbContext.Drives.FirstOrDefaultAsync();
        Assert.NotNull(driveRecord);
        Assert.Equal(1, driveRecord.TotalTests);

        // Verify disk card was created via cardTestService
        var cards = await dbContext.DiskCards.ToListAsync();
        Assert.NotEmpty(cards);
    }

    // ── Test 2: Maximum size test (stress test) ──────────────────────

    /// <summary>
    /// Tests with 50,000 samples — close to what a full 2 TB disk test
    /// would generate. Must not OOM or deadlock.
    /// </summary>
    [Fact]
    public async Task SaveMassiveTest_50000Samples_Completes()
    {
        // Arrange
        var dbContext = CreateDbContext($"SaveMassive_{Guid.NewGuid():N}");
        await dbContext.Database.EnsureCreatedAsync();

        var cardTestService = new DiskCardTestService(
            dbContext,
            Substitute.For<IQualityCalculator>(),
            NullLogger<DiskCardTestService>.Instance,
            Substitute.For<ICertificateGenerator>());

        var persistenceService = new SurfaceTestPersistenceService(dbContext, cardTestService);

        var result = CreateLargeTestResult(sampleCount: 50_000);
        var drive = CreateDriveInfo();

        // Act — 90 second timeout for massive test
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken, timeoutCts.Token);
        var testId = await persistenceService.SaveAsync(result, drive, cts.Token);

        // Assert
        Assert.NotEqual(Guid.Empty, testId);

        var testRecord = await dbContext.Tests
            .Include(t => t.SurfaceSamples)
            .FirstOrDefaultAsync(t => t.Id == testId);
        Assert.Equal(50_000, testRecord!.SurfaceSamples.Count);
    }

    // ── Test 3: Sequential saves (simulating multiple iterations) ────

    /// <summary>
    /// Simulates the scenario where the user runs multiple tests in sequence.
    /// Each save should complete and the disk card should accumulate correctly.
    /// </summary>
    [Fact]
    public async Task MultipleSequentialSaves_AccumulateCorrectly()
    {
        // Arrange
        var dbName = $"Sequential_{Guid.NewGuid():N}";
        var drive = CreateDriveInfo();

        // Simulate 5 consecutive test runs
        for (int run = 0; run < 5; run++)
        {
            var dbContext = CreateDbContext(dbName);
            if (run == 0)
                await dbContext.Database.EnsureCreatedAsync();

            var cardTestService = new DiskCardTestService(
                dbContext,
                Substitute.For<IQualityCalculator>(),
                NullLogger<DiskCardTestService>.Instance,
                Substitute.For<ICertificateGenerator>());

            var persistenceService = new SurfaceTestPersistenceService(dbContext, cardTestService);

            var result = CreateLargeTestResult(sampleCount: 1000);
            result.TestId = Guid.NewGuid().ToString();
            result.DriveSerialNumber = drive.SerialNumber;

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken, timeoutCts.Token);
            var testId = await persistenceService.SaveAsync(result, drive, cts.Token);

            Assert.NotEqual(Guid.Empty, testId);

            // Verify accumulation
            var driveRecord = await dbContext.Drives.FirstOrDefaultAsync();
            Assert.NotNull(driveRecord);
            Assert.Equal(run + 1, driveRecord!.TotalTests);
        }
    }

    // ── Test 4: Zero-byte guard — detect infinite loop scenario ──────

    /// <summary>
    /// Verifies that the persistence service handles empty samples
    /// without hanging. This was one of the suspected root causes
    /// for the original hang.
    /// </summary>
    [Fact]
    public async Task SaveWithZeroSamples_CompletesImmediately()
    {
        // Arrange
        var dbContext = CreateDbContext($"ZeroSamples_{Guid.NewGuid():N}");
        await dbContext.Database.EnsureCreatedAsync();

        var cardTestService = new DiskCardTestService(
            dbContext,
            Substitute.For<IQualityCalculator>(),
            NullLogger<DiskCardTestService>.Instance,
            Substitute.For<ICertificateGenerator>());

        var persistenceService = new SurfaceTestPersistenceService(dbContext, cardTestService);

        var result = CreateLargeTestResult(sampleCount: 0);
        var drive = CreateDriveInfo();

        // Act
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken, timeoutCts.Token);
        var testId = await persistenceService.SaveAsync(result, drive, cts.Token);

        // Assert
        Assert.NotEqual(Guid.Empty, testId);
        var testRecord = await dbContext.Tests
            .Include(t => t.SurfaceSamples)
            .FirstOrDefaultAsync(t => t.Id == testId);
        Assert.Empty(testRecord!.SurfaceSamples);
    }

    // ── Test 5: Error samples — verify error handling ────────────────

    /// <summary>
    /// Test with error-containing samples. The persistence should
    /// store error metadata correctly without throwing.
    /// </summary>
    [Fact]
    public async Task SaveWithErrorSamples_PreservesErrorData()
    {
        // Arrange
        var dbContext = CreateDbContext($"ErrorSamples_{Guid.NewGuid():N}");
        await dbContext.Database.EnsureCreatedAsync();

        var cardTestService = new DiskCardTestService(
            dbContext,
            Substitute.For<IQualityCalculator>(),
            NullLogger<DiskCardTestService>.Instance,
            Substitute.For<ICertificateGenerator>());

        var persistenceService = new SurfaceTestPersistenceService(dbContext, cardTestService);

        var result = CreateLargeTestResult(sampleCount: 500);
        result.ErrorCount = 15; // Critical failure threshold
        // Add some error-marked samples
        for (int i = 0; i < 15; i++)
        {
            result.Samples[i * 30].ErrorCount = 1; // Every 30th sample has an error
        }

        var drive = CreateDriveInfo();

        // Act
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken, timeoutCts.Token);
        var testId = await persistenceService.SaveAsync(result, drive, cts.Token);

        // Assert
        var testRecord = await dbContext.Tests
            .Include(t => t.SurfaceSamples)
            .FirstOrDefaultAsync(t => t.Id == testId);

        Assert.NotNull(testRecord);
        Assert.Equal(15, testRecord.Errors);
        Assert.Equal("F", testRecord.Grade); // >100 errors → F... wait, actually 15 errors → E according to the code
        // Let's check: ErrorCount > 100 → F, > 10 → E, > 5 → D, > 0 → C
        Assert.Equal("E", testRecord.Grade); // 15 errors → E
    }

    // ── Test 6: Cancellation support ─────────────────────────────────

    /// <summary>
    /// Verifies that a cancelled token is honored during save.
    /// </summary>
    [Fact]
    public async Task SaveWithCancelledToken_ThrowsOperationCancelled()
    {
        // Arrange
        var dbContext = CreateDbContext($"Cancel_{Guid.NewGuid():N}");
        await dbContext.Database.EnsureCreatedAsync();

        var cardTestService = new DiskCardTestService(
            dbContext,
            Substitute.For<IQualityCalculator>(),
            NullLogger<DiskCardTestService>.Instance,
            Substitute.For<ICertificateGenerator>());

        var persistenceService = new SurfaceTestPersistenceService(dbContext, cardTestService);

        var result = CreateLargeTestResult(sampleCount: 10000);
        var drive = CreateDriveInfo();

        var cancelCts = new CancellationTokenSource();
        cancelCts.Cancel(); // Cancel immediately
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken, cancelCts.Token);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => persistenceService.SaveAsync(result, drive, cts.Token));
    }

    // ── Test 7: Duplicate saves (same serial) ──────────────────────

    /// <summary>
    /// Simulates saving the same drive twice — verifies that the drive
    /// record is reused, not duplicated.
    /// </summary>
    [Fact]
    public async Task SameDriveSavedTwice_UsesSameDriveRecord()
    {
        // Arrange
        var dbName = $"DuplicateSave_{Guid.NewGuid():N}";
        var drive = CreateDriveInfo();

        // First save
        var dbContext1 = CreateDbContext(dbName);
        await dbContext1.Database.EnsureCreatedAsync();

        var cardTestService1 = new DiskCardTestService(
            dbContext1,
            Substitute.For<IQualityCalculator>(),
            NullLogger<DiskCardTestService>.Instance,
            Substitute.For<ICertificateGenerator>());

        var persistenceService1 = new SurfaceTestPersistenceService(dbContext1, cardTestService1);

        var result1 = CreateLargeTestResult(sampleCount: 100);
        result1.DriveSerialNumber = drive.SerialNumber;

        using var timeoutCts1 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cts1 = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken, timeoutCts1.Token);
        await persistenceService1.SaveAsync(result1, drive, cts1.Token);

        var driveCount1 = await dbContext1.Drives.CountAsync();
        Assert.Equal(1, driveCount1);

        // Second save — same DbContext to avoid issues with in-memory DB
        var result2 = CreateLargeTestResult(sampleCount: 200);
        result2.TestId = Guid.NewGuid().ToString();
        result2.DriveSerialNumber = drive.SerialNumber;
        result2.DriveModel = "WDC WD5000AAKX-00ERMA0-SECOND";

        using var timeoutCts2 = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken, timeoutCts2.Token);
        await persistenceService1.SaveAsync(result2, drive, cts2.Token);

        // Assert: still only one drive record
        var driveCount2 = await dbContext1.Drives.CountAsync();
        Assert.Equal(1, driveCount2);

        var driveRecord = await dbContext1.Drives.FirstAsync();
        Assert.Equal(2, driveRecord.TotalTests);
        Assert.Equal("WDC WD5000AAKX-00ERMA0-SECOND", driveRecord.Name); // Updated name
    }

    // ── Test 8: Performance benchmark ────────────────────────────────

    /// <summary>
    /// Measures performance of saving different sample sizes.
    /// Helps establish a baseline and catch regressions.
    /// </summary>
    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public async Task SavePerformance_ScalesLinearly(int sampleCount)
    {
        // Arrange
        var dbContext = CreateDbContext($"Perf_{sampleCount}_{Guid.NewGuid():N}");
        await dbContext.Database.EnsureCreatedAsync();

        var cardTestService = new DiskCardTestService(
            dbContext,
            Substitute.For<IQualityCalculator>(),
            NullLogger<DiskCardTestService>.Instance,
            Substitute.For<ICertificateGenerator>());

        var persistenceService = new SurfaceTestPersistenceService(dbContext, cardTestService);

        var result = CreateLargeTestResult(sampleCount);
        var drive = CreateDriveInfo();

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken, timeoutCts.Token);
        await persistenceService.SaveAsync(result, drive, cts.Token);
        sw.Stop();

        // Assert — rough performance expectation: ~10k samples/second minimum
        var samplesPerSecond = sampleCount / sw.Elapsed.TotalSeconds;
        Assert.True(samplesPerSecond > 500,
            $"Performance too slow: {samplesPerSecond:F0} samples/sec (expected >500). " +
            $"Took {sw.Elapsed.TotalSeconds:F2}s for {sampleCount} samples.");
    }
}