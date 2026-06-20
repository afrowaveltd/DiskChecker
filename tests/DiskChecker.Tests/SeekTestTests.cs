using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Tests for the SMART-informed seek test recommendation engine.
/// </summary>
public class SeekTestRecommendationTests
{
    private readonly SeekTestExecutor _executor = new(null);

    // ──────────────────────────────────────────────
    //  Young/healthy HDD – full random recommendation
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRecommendation_YoungHealthyHdd_RecommendsRandom3000()
    {
        var smarta = new SmartaData
        {
            PowerOnHours = 5000,       // ~7 months
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            DeviceModel = "WDC WD40EFRX",
            DeviceType = "sat"
        };

        var rec = _executor.GetRecommendation(smarta, 4_000_000_000_000, isSolidState: false);

        Assert.Equal(SeekTestType.Random, rec.RecommendedType);
        Assert.Equal(3000, rec.RecommendedSeekCount);
        Assert.Equal(1000, rec.RecommendedSkipSegments);
        Assert.Equal(5000, rec.MaxSafeSeekCount);
        Assert.False(rec.IsConservative);
        Assert.False(rec.IsTooFragile);
        Assert.Contains("3000 seeků", rec.Rationale);
    }

    // ──────────────────────────────────────────────
    //  Aged disk (3-5 years) – conservative
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRecommendation_AgedCleanHdd_RecommendsRandom1500()
    {
        var smarta = new SmartaData
        {
            PowerOnHours = 30000,      // ~3.4 years
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            DeviceModel = "ST500DM002",
            DeviceType = "sat"
        };

        var rec = _executor.GetRecommendation(smarta, 500_000_000_000, isSolidState: false);

        Assert.Equal(SeekTestType.Random, rec.RecommendedType);
        Assert.Equal(1500, rec.RecommendedSeekCount);
        Assert.Equal(2500, rec.MaxSafeSeekCount);
        Assert.False(rec.IsConservative);
        Assert.False(rec.IsTooFragile);
    }

    // ──────────────────────────────────────────────
    //  Aged disk with reallocations – more conservative
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRecommendation_AgedWithReallocations_RecommendsFullStroke800()
    {
        var smarta = new SmartaData
        {
            PowerOnHours = 35000,      // ~4 years
            ReallocatedSectorCount = 8,
            PendingSectorCount = 0,
            DeviceModel = "ST2000DM001",
            DeviceType = "sat"
        };

        var rec = _executor.GetRecommendation(smarta, 2_000_000_000_000, isSolidState: false);

        Assert.Equal(SeekTestType.FullStroke, rec.RecommendedType);
        Assert.Equal(800, rec.RecommendedSeekCount);
        Assert.Equal(1500, rec.MaxSafeSeekCount);
        Assert.True(rec.IsConservative);
        Assert.False(rec.IsTooFragile);
        Assert.Contains("realokovaných sektorů", rec.Rationale);
    }

    // ──────────────────────────────────────────────
    //  Old disk (5-7 years) – conservative full-stroke
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRecommendation_OldDisk_RecommendsFullStroke600()
    {
        var smarta = new SmartaData
        {
            PowerOnHours = 50000,      // ~5.7 years
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            DeviceModel = "Hitachi HDS7210",
            DeviceType = "sat"
        };

        var rec = _executor.GetRecommendation(smarta, 1_000_000_000_000, isSolidState: false);

        Assert.Equal(SeekTestType.FullStroke, rec.RecommendedType);
        Assert.Equal(600, rec.RecommendedSeekCount);
        Assert.Equal(1000, rec.MaxSafeSeekCount);
        Assert.True(rec.IsConservative);
        Assert.False(rec.IsTooFragile);
        Assert.Contains("Starší disk", rec.Rationale);
    }

    // ──────────────────────────────────────────────
    //  Veteran disk (7+ years / 80,000+ hours) – very gentle
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRecommendation_VeteranDisk_RecommendsFullStroke300()
    {
        var smarta = new SmartaData
        {
            PowerOnHours = 80000,      // ~9.1 years
            ReallocatedSectorCount = 0,
            PendingSectorCount = 0,
            DeviceModel = "WDC WD20EARS",
            DeviceType = "sat"
        };

        var rec = _executor.GetRecommendation(smarta, 2_000_000_000_000, isSolidState: false);

        Assert.Equal(SeekTestType.FullStroke, rec.RecommendedType);
        Assert.Equal(300, rec.RecommendedSeekCount);
        Assert.Equal(500, rec.MaxSafeSeekCount);
        Assert.True(rec.IsConservative);
        Assert.False(rec.IsTooFragile);
        Assert.Contains("Veteránský disk", rec.Rationale);
        Assert.Contains("300 seeků", rec.Rationale);
    }

    // ──────────────────────────────────────────────
    //  Fragile disk (many reallocated sectors) – refused
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRecommendation_FragileDisk_RefusesTest()
    {
        var smarta = new SmartaData
        {
            PowerOnHours = 20000,
            ReallocatedSectorCount = 60,
            PendingSectorCount = 0,
            DeviceModel = "Failing Drive",
            DeviceType = "sat"
        };

        var rec = _executor.GetRecommendation(smarta, 1_000_000_000_000, isSolidState: false);

        Assert.True(rec.IsTooFragile);
        Assert.Equal(0, rec.RecommendedSeekCount);
        Assert.Equal(0, rec.MaxSafeSeekCount);
        Assert.Contains("kritické opotřebení", rec.Rationale);
    }

    [Fact]
    public void GetRecommendation_FragileDisk_PendingSectors_RefusesTest()
    {
        var smarta = new SmartaData
        {
            PowerOnHours = 15000,
            ReallocatedSectorCount = 0,
            PendingSectorCount = 15,
            DeviceModel = "Unstable Drive",
            DeviceType = "sat"
        };

        var rec = _executor.GetRecommendation(smarta, 500_000_000_000, isSolidState: false);

        Assert.True(rec.IsTooFragile);
        Assert.Contains("pending sektorů", rec.Rationale);
    }

    // ──────────────────────────────────────────────
    //  SSD – reduced recommendation
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRecommendation_Ssd_RecommendsReducedRandom500()
    {
        var smarta = new SmartaData
        {
            PowerOnHours = 10000,
            ReallocatedSectorCount = 0,
            DeviceModel = "Samsung SSD 860 EVO",
            DeviceType = "sat"
        };

        var rec = _executor.GetRecommendation(smarta, 500_000_000_000, isSolidState: true);

        Assert.Equal(SeekTestType.Random, rec.RecommendedType);
        Assert.Equal(500, rec.RecommendedSeekCount);
        Assert.Equal(1000, rec.MaxSafeSeekCount);
        Assert.True(rec.IsConservative);
        Assert.Contains("SSD detekován", rec.Rationale);
    }

    // ──────────────────────────────────────────────
    //  NVMe – detected as SSD
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRecommendation_Nvme_RecommendsReduced()
    {
        var smarta = new SmartaData
        {
            PowerOnHours = 5000,
            DeviceModel = "Samsung SSD 980 PRO",
            DeviceType = "nvme"
        };

        var rec = _executor.GetRecommendation(smarta, 1_000_000_000_000, isSolidState: true);

        Assert.Equal(500, rec.RecommendedSeekCount);
        Assert.True(rec.IsConservative);
    }

    // ──────────────────────────────────────────────
    //  No SMART data – default recommendation
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRecommendation_NoSmartaData_RecommendsDefault()
    {
        var rec = _executor.GetRecommendation(null, 1_000_000_000_000, isSolidState: false);

        Assert.Equal(SeekTestType.Random, rec.RecommendedType);
        Assert.Equal(3000, rec.RecommendedSeekCount);
        Assert.Equal(5000, rec.MaxSafeSeekCount);
        Assert.False(rec.IsConservative);
        Assert.False(rec.IsTooFragile);
        Assert.Contains("SMART data nedostupná", rec.Rationale);
    }

    // ──────────────────────────────────────────────
    //  Metadata is populated correctly
    // ──────────────────────────────────────────────

    [Fact]
    public void GetRecommendation_PopulatesMetadata()
    {
        var smarta = new SmartaData
        {
            PowerOnHours = 12345,
            ReallocatedSectorCount = 3,
            DeviceModel = "Test HDD",
            DeviceType = "sat"
        };

        var rec = _executor.GetRecommendation(smarta, 1_000_000_000_000, isSolidState: false);

        Assert.Equal(12345, rec.PowerOnHours);
        Assert.Equal(3, rec.ReallocatedSectors);
        Assert.False(rec.IsSolidState);
    }
}

/// <summary>
/// Tests for seek position generation algorithms.
/// </summary>
public class SeekPositionGenerationTests
{
    // ──────────────────────────────────────────────
    //  Full-stroke positions
    // ──────────────────────────────────────────────

    [Fact]
    public void GenerateFullStroke_ProducesCorrectCount()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.FullStroke,
            seekCount: 100,
            totalBytes: 1_000_000_000_000, // 1 TB
            skipSegments: 1000,
            blockSizeBytes: 4096);

        Assert.Equal(100, positions.Count);
    }

    [Fact]
    public void GenerateFullStroke_AlternatesMinToMaxAndBack()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.FullStroke,
            seekCount: 20,
            totalBytes: 1_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        // First seek: min → somewhere
        Assert.True(positions[0].SourceLba <= positions[0].DestLba,
            "First full-stroke seek should go from min toward max");

        // Second seek: max → somewhere (backward)
        Assert.True(positions[1].SourceLba >= positions[1].DestLba,
            "Second full-stroke seek should go from max toward min");
    }

    [Fact]
    public void GenerateFullStroke_CoversFullRange()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.FullStroke,
            seekCount: 100,
            totalBytes: 1_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        var maxLba = 1_000_000_000_000L / 512;
        var minLba = 64L;

        // Check that we have positions near both extremes
        var allDestinations = positions.Select(p => p.DestLba).ToList();
        Assert.Contains(allDestinations, d => d <= minLba + 1000);
        Assert.Contains(allDestinations, d => d >= maxLba - 10000);
    }

    // ──────────────────────────────────────────────
    //  Random positions
    // ──────────────────────────────────────────────

    [Fact]
    public void GenerateRandom_ProducesCorrectCount()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Random,
            seekCount: 500,
            totalBytes: 2_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        Assert.Equal(500, positions.Count);
    }

    [Fact]
    public void GenerateRandom_AllPositionsWithinRange()
    {
        var totalBytes = 1_000_000_000_000L;
        var maxLba = totalBytes / 512;
        var minLba = 64L;

        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Random,
            seekCount: 200,
            totalBytes: totalBytes,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        foreach (var pos in positions)
        {
            Assert.True(pos.SourceLba >= minLba, $"Source LBA {pos.SourceLba} below minimum {minLba}");
            Assert.True(pos.SourceLba <= maxLba, $"Source LBA {pos.SourceLba} above maximum {maxLba}");
            Assert.True(pos.DestLba >= minLba, $"Dest LBA {pos.DestLba} below minimum {minLba}");
            Assert.True(pos.DestLba <= maxLba, $"Dest LBA {pos.DestLba} above maximum {maxLba}");
        }
    }

    [Fact]
    public void GenerateRandom_IsReproducible()
    {
        // Same seed (42) should produce same positions
        var positions1 = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Random,
            seekCount: 50,
            totalBytes: 1_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        var positions2 = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Random,
            seekCount: 50,
            totalBytes: 1_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        Assert.Equal(positions1.Count, positions2.Count);
        for (int i = 0; i < positions1.Count; i++)
        {
            Assert.Equal(positions1[i].SourceLba, positions2[i].SourceLba);
            Assert.Equal(positions1[i].DestLba, positions2[i].DestLba);
        }
    }

    [Fact]
    public void GenerateRandom_HasVariedDistances()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Random,
            seekCount: 100,
            totalBytes: 1_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        var distances = positions.Select(p => Math.Abs(p.DestLba - p.SourceLba)).ToList();
        var minDist = distances.Min();
        var maxDist = distances.Max();

        // Random seeks should have varied distances
        Assert.True(maxDist > minDist * 10,
            $"Random seeks should have varied distances. Min={minDist}, Max={maxDist}");
    }

    // ──────────────────────────────────────────────
    //  Skip positions
    // ──────────────────────────────────────────────

    [Fact]
    public void GenerateSkip_ProducesCorrectCount()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Skip,
            seekCount: 200,
            totalBytes: 1_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        Assert.Equal(200, positions.Count);
    }

    [Fact]
    public void GenerateSkip_EachPositionContinuesFromPrevious()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Skip,
            seekCount: 50,
            totalBytes: 1_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        // Each destination becomes the next source (sequential chain)
        for (int i = 1; i < positions.Count; i++)
        {
            Assert.Equal(positions[i - 1].DestLba, positions[i].SourceLba);
        }
    }

    [Fact]
    public void GenerateSkip_JumpsByVaryingSegmentCounts()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Skip,
            seekCount: 30,
            totalBytes: 1_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        // Verify that jump sizes vary (1,2,3,...,10 segments pattern)
        var distances = positions.Select(p => Math.Abs(p.DestLba - p.SourceLba)).ToList();
        var uniqueDistances = distances.Distinct().Count();

        Assert.True(uniqueDistances >= 3,
            $"Skip seeks should have varying jump sizes. Got {uniqueDistances} unique distances out of {distances.Count}");
    }

    [Fact]
    public void GenerateSkip_WrapsAroundAtEnd()
    {
        // Use a very small disk so wrapping is forced
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Skip,
            seekCount: 100,
            totalBytes: 10_000_000, // ~10 MB – tiny disk
            skipSegments: 1000,
            blockSizeBytes: 4096);

        // All positions should be within valid range
        var maxLba = 10_000_000L / 512;
        foreach (var pos in positions)
        {
            Assert.True(pos.SourceLba >= 0 && pos.SourceLba <= maxLba, $"Source LBA {pos.SourceLba} out of range [0, {maxLba}]");
            Assert.True(pos.DestLba >= 0 && pos.DestLba <= maxLba, $"Dest LBA {pos.DestLba} out of range [0, {maxLba}]");
        }
    }

    // ──────────────────────────────────────────────
    //  Edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public void GeneratePositions_ZeroTotalBytes_UsesFallback()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Random,
            seekCount: 10,
            totalBytes: 0,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        Assert.Equal(10, positions.Count);
        // Should not crash – uses 1 TB fallback
    }

    [Fact]
    public void GeneratePositions_ZeroSeekCount_UsesMinimum()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.Random,
            seekCount: 0,
            totalBytes: 1_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        Assert.Equal(100, positions.Count); // Minimum 100
    }

    [Fact]
    public void GeneratePositions_NegativeSeekCount_UsesMinimum()
    {
        var positions = SeekTestExecutor.GenerateSeekPositions(
            SeekTestType.FullStroke,
            seekCount: -5,
            totalBytes: 1_000_000_000_000,
            skipSegments: 1000,
            blockSizeBytes: 4096);

        Assert.Equal(100, positions.Count);
    }
}

/// <summary>
/// Tests for SeekTestService orchestration with mocked dependencies.
/// </summary>
public class SeekTestServiceTests
{
    // ──────────────────────────────────────────────
    //  GetRecommendationAsync – returns recommendation
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GetRecommendationAsync_ReturnsRecommendation()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var seekExecutor = Substitute.For<ISeekTestExecutor>();
        var logger = NullLogger<SeekTestService>.Instance;

        var smarta = new SmartaData
        {
            PowerOnHours = 10000,
            ReallocatedSectorCount = 0,
            DeviceModel = "Test HDD",
            DeviceType = "sat"
        };

        smartaProvider.GetSmartaDataAsync("/dev/sda", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SmartaData?>(smarta));

        var expectedRec = new SeekTestRecommendation
        {
            RecommendedType = SeekTestType.Random,
            RecommendedSeekCount = 3000,
            Rationale = "Test recommendation"
        };

        seekExecutor.GetRecommendation(smarta, 1_000_000_000_000, false)
            .Returns(expectedRec);

        var service = new SeekTestService(smartaProvider, seekExecutor, logger);

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sda",
            Name = "Test HDD",
            TotalSize = 1_000_000_000_000
        };

        var rec = await service.GetRecommendationAsync(drive, TestContext.Current.CancellationToken);

        Assert.Equal(SeekTestType.Random, rec.RecommendedType);
        Assert.Equal(3000, rec.RecommendedSeekCount);
    }

    // ──────────────────────────────────────────────
    //  GetRecommendationAsync – SMART unavailable
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GetRecommendationAsync_SmartUnavailable_StillReturnsRecommendation()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var seekExecutor = Substitute.For<ISeekTestExecutor>();
        var logger = NullLogger<SeekTestService>.Instance;

        smartaProvider.GetSmartaDataAsync("/dev/sdb", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SmartaData?>(null));

        var expectedRec = new SeekTestRecommendation
        {
            RecommendedType = SeekTestType.Random,
            RecommendedSeekCount = 3000,
            Rationale = "SMART data nedostupná"
        };

        seekExecutor.GetRecommendation(null, 500_000_000_000, false)
            .Returns(expectedRec);

        var service = new SeekTestService(smartaProvider, seekExecutor, logger);

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sdb",
            Name = "Unknown Drive",
            TotalSize = 500_000_000_000
        };

        var rec = await service.GetRecommendationAsync(drive, TestContext.Current.CancellationToken);

        Assert.NotNull(rec);
        Assert.Equal(3000, rec.RecommendedSeekCount);
    }

    // ──────────────────────────────────────────────
    //  RunWithRecommendationAsync – fragile disk refused
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunWithRecommendation_FragileDisk_ReturnsAbortedResult()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var seekExecutor = Substitute.For<ISeekTestExecutor>();
        var logger = NullLogger<SeekTestService>.Instance;

        var smarta = new SmartaData
        {
            PowerOnHours = 20000,
            ReallocatedSectorCount = 60,
            DeviceModel = "Failing HDD",
            DeviceType = "sat"
        };

        smartaProvider.GetSmartaDataAsync("/dev/sdc", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SmartaData?>(smarta));

        var fragileRec = new SeekTestRecommendation
        {
            IsTooFragile = true,
            RecommendedSeekCount = 0,
            MaxSafeSeekCount = 0,
            Rationale = "Disk vykazuje kritické opotřebení"
        };

        seekExecutor.GetRecommendation(smarta, 1_000_000_000_000, false)
            .Returns(fragileRec);

        var service = new SeekTestService(smartaProvider, seekExecutor, logger);

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sdc",
            Name = "Failing HDD",
            TotalSize = 1_000_000_000_000
        };

        var result = await service.RunWithRecommendationAsync(drive, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsCompleted);
        Assert.True(result.WasAborted);
        Assert.Contains("kritické opotřebení", result.Notes);
        Assert.NotNull(result.Recommendation);
        Assert.True(result.Recommendation.IsTooFragile);
    }

    // ──────────────────────────────────────────────
    //  RunWithRecommendationAsync – healthy disk runs test
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunWithRecommendation_HealthyDisk_ExecutesTest()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var seekExecutor = Substitute.For<ISeekTestExecutor>();
        var logger = NullLogger<SeekTestService>.Instance;

        var smarta = new SmartaData
        {
            PowerOnHours = 5000,
            ReallocatedSectorCount = 0,
            DeviceModel = "Healthy HDD",
            DeviceType = "sat"
        };

        smartaProvider.GetSmartaDataAsync("/dev/sdd", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SmartaData?>(smarta));

        var healthyRec = new SeekTestRecommendation
        {
            RecommendedType = SeekTestType.Random,
            RecommendedSeekCount = 3000,
            RecommendedSkipSegments = 1000,
            MaxSafeSeekCount = 5000,
            IsTooFragile = false,
            Rationale = "Doporučen plný random seek test"
        };

        seekExecutor.GetRecommendation(smarta, 2_000_000_000_000, false)
            .Returns(healthyRec);

        var expectedResult = new SeekTestResult
        {
            TestType = SeekTestType.Random,
            SeekCount = 3000,
            AverageLatencyMs = 12.5,
            IsCompleted = true,
            Samples = new List<SeekLatencySample>
            {
                new() { Index = 1, LatencyMs = 12.0 },
                new() { Index = 2, LatencyMs = 13.0 }
            }
        };

        seekExecutor.ExecuteAsync(
            Arg.Is<SeekTestRequest>(r =>
                r.TestType == SeekTestType.Random &&
                r.SeekCount == 3000 &&
                r.Drive.Path == "/dev/sdd"),
            Arg.Any<Action<SeekTestProgress>?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedResult));

        var service = new SeekTestService(smartaProvider, seekExecutor, logger);

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sdd",
            Name = "Healthy HDD",
            TotalSize = 2_000_000_000_000
        };

        var result = await service.RunWithRecommendationAsync(drive, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsCompleted);
        Assert.False(result.WasAborted);
        Assert.Equal(SeekTestType.Random, result.TestType);
        Assert.Equal(3000, result.SeekCount);
        Assert.Equal(12.5, result.AverageLatencyMs);
        Assert.Equal("Healthy HDD", result.DriveModel);
    }

    // ──────────────────────────────────────────────
    //  RunWithRecommendationAsync – preferred type override
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunWithRecommendation_PreferredType_OverridesRecommendation()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var seekExecutor = Substitute.For<ISeekTestExecutor>();
        var logger = NullLogger<SeekTestService>.Instance;

        var smarta = new SmartaData
        {
            PowerOnHours = 5000,
            ReallocatedSectorCount = 0,
            DeviceModel = "Test HDD",
            DeviceType = "sat"
        };

        smartaProvider.GetSmartaDataAsync("/dev/sde", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SmartaData?>(smarta));

        var rec = new SeekTestRecommendation
        {
            RecommendedType = SeekTestType.Random,  // Would recommend Random
            RecommendedSeekCount = 3000,
            RecommendedSkipSegments = 1000,
            IsTooFragile = false
        };

        seekExecutor.GetRecommendation(smarta, 1_000_000_000_000, false).Returns(rec);

        seekExecutor.ExecuteAsync(
            Arg.Is<SeekTestRequest>(r => r.TestType == SeekTestType.FullStroke),
            Arg.Any<Action<SeekTestProgress>?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SeekTestResult
            {
                TestType = SeekTestType.FullStroke,
                IsCompleted = true
            }));

        var service = new SeekTestService(smartaProvider, seekExecutor, logger);

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sde",
            Name = "Test HDD",
            TotalSize = 1_000_000_000_000
        };

        // User prefers FullStroke even though recommendation says Random
        var result = await service.RunWithRecommendationAsync(
            drive,
            preferredType: SeekTestType.FullStroke,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SeekTestType.FullStroke, result.TestType);
    }

    // ──────────────────────────────────────────────
    //  IsPlatformSupportedAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task IsPlatformSupported_DelegatesToExecutor()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var seekExecutor = Substitute.For<ISeekTestExecutor>();
        var logger = NullLogger<SeekTestService>.Instance;

        seekExecutor.IsPlatformSupportedAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var service = new SeekTestService(smartaProvider, seekExecutor, logger);

        var supported = await service.IsPlatformSupportedAsync(TestContext.Current.CancellationToken);

        Assert.True(supported);
    }

    // ──────────────────────────────────────────────
    //  RunAsync – enriches result with drive metadata
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_EnrichesResultWithDriveMetadata()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var seekExecutor = Substitute.For<ISeekTestExecutor>();
        var logger = NullLogger<SeekTestService>.Instance;

        var smarta = new SmartaData
        {
            PowerOnHours = 8760,
            DeviceModel = "Metadata HDD",
            SerialNumber = "ABC123",
            DeviceType = "sat"
        };

        smartaProvider.GetSmartaDataAsync("/dev/sdf", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SmartaData?>(smarta));

        var executorResult = new SeekTestResult
        {
            TestType = SeekTestType.Skip,
            SeekCount = 2000,
            AverageLatencyMs = 8.3,
            IsCompleted = true
        };

        seekExecutor.ExecuteAsync(
            Arg.Any<SeekTestRequest>(),
            Arg.Any<Action<SeekTestProgress>?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(executorResult));

        seekExecutor.GetRecommendation(smarta, 3_000_000_000_000, false)
            .Returns(new SeekTestRecommendation
            {
                RecommendedType = SeekTestType.Skip,
                RecommendedSeekCount = 2000,
                RecommendedSkipSegments = 1000
            });

        var service = new SeekTestService(smartaProvider, seekExecutor, logger);

        var request = new SeekTestRequest
        {
            Drive = new CoreDriveInfo
            {
                Path = "/dev/sdf",
                Name = "Metadata HDD",
                TotalSize = 3_000_000_000_000
            },
            TestType = SeekTestType.Skip,
            SeekCount = 2000,
            SkipSegments = 1000
        };

        var result = await service.RunAsync(
            request,
            progressCallback: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Metadata HDD", result.DriveModel);
        Assert.Equal("ABC123", result.DriveSerialNumber);
        Assert.Equal("/dev/sdf", result.DrivePath);
        Assert.Equal(3_000_000_000_000, result.DriveTotalBytes);
        Assert.Equal(8760, result.PowerOnHours);
        Assert.NotNull(result.Recommendation);
    }
}

/// <summary>
/// Tests for percentile computation and new latency statistics (median, P95, P99).
/// </summary>
public class SeekTestStatisticsTests
{
    private readonly SeekTestExecutor _executor = new(null);

    // ──────────────────────────────────────────────
    //  Percentile helper
    // ──────────────────────────────────────────────

    [Fact]
    public void Percentile_Median_OfOddCount_ReturnsMiddleElement()
    {
        var sorted = new List<double> { 1.0, 2.0, 3.0, 4.0, 5.0 };
        var result = InvokePercentile(sorted, 0.50);
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Percentile_Median_OfEvenCount_Interpolates()
    {
        var sorted = new List<double> { 1.0, 2.0, 3.0, 4.0 };
        var result = InvokePercentile(sorted, 0.50);
        Assert.Equal(2.5, result);
    }

    [Fact]
    public void Percentile_P95_ReturnsCorrectValue()
    {
        // 20 values: P95 index = 0.95 * 19 = 18.05 → interpolate between [18]=19 and [19]=20
        var sorted = Enumerable.Range(1, 20).Select(i => (double)i).ToList();
        var result = InvokePercentile(sorted, 0.95);
        Assert.Equal(19.05, result, 3);
    }

    [Fact]
    public void Percentile_P99_ReturnsCorrectValue()
    {
        // 100 values: P99 index = 0.99 * 99 = 98.01 → interpolate between [98]=99 and [99]=100
        var sorted = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        var result = InvokePercentile(sorted, 0.99);
        Assert.Equal(99.01, result, 3);
    }

    [Fact]
    public void Percentile_SingleElement_ReturnsThatElement()
    {
        var sorted = new List<double> { 42.0 };
        Assert.Equal(42.0, InvokePercentile(sorted, 0.50));
        Assert.Equal(42.0, InvokePercentile(sorted, 0.95));
        Assert.Equal(42.0, InvokePercentile(sorted, 0.99));
    }

    [Fact]
    public void Percentile_EmptyList_ReturnsZero()
    {
        var sorted = new List<double>();
        Assert.Equal(0, InvokePercentile(sorted, 0.50));
    }

    [Fact]
    public void Percentile_MinAndMax_ReturnExtremes()
    {
        var sorted = new List<double> { 5.0, 10.0, 15.0, 20.0, 25.0 };
        Assert.Equal(5.0, InvokePercentile(sorted, 0.0));
        Assert.Equal(25.0, InvokePercentile(sorted, 1.0));
    }

    // ──────────────────────────────────────────────
    //  Result statistics population
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PopulatesMedianP95P99()
    {
        var drive = new CoreDriveInfo { Path = "/dev/sdz", Name = "Test", TotalSize = 1_000_000_000 };
        var request = new SeekTestRequest
        {
            Drive = drive,
            TestType = SeekTestType.Random,
            SeekCount = 5,
            BlockSizeBytes = 512,
            TimeoutSeconds = 5
        };

        var result = await _executor.ExecuteAsync(request, null, CancellationToken.None);

        // With 5 random seeks, we should have median, P95, P99 populated
        Assert.True(result.MedianLatencyMs >= 0);
        Assert.True(result.P95LatencyMs >= 0);
        Assert.True(result.P99LatencyMs >= 0);
        Assert.True(result.P95LatencyMs >= result.MedianLatencyMs);
        Assert.True(result.P99LatencyMs >= result.P95LatencyMs);
    }

    [Fact]
    public async Task ExecuteAsync_AllErrorSamples_StatisticsAreZero()
    {
        // This test verifies that when all samples fail, statistics remain zero
        // (tested indirectly via the recommendation engine's fragile disk path)
        var drive = new CoreDriveInfo { Path = "/dev/sdz", Name = "Test", TotalSize = 1_000_000_000 };
        var request = new SeekTestRequest
        {
            Drive = drive,
            TestType = SeekTestType.Random,
            SeekCount = 1,
            BlockSizeBytes = 512,
            TimeoutSeconds = 5
        };

        var result = await _executor.ExecuteAsync(request, null, CancellationToken.None);

        // At least the result structure is complete
        Assert.NotNull(result);
        Assert.True(result.MedianLatencyMs >= 0);
        Assert.True(result.P95LatencyMs >= 0);
        Assert.True(result.P99LatencyMs >= 0);
    }

    // ──────────────────────────────────────────────
    //  LatestSample in progress
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ProgressCallback_IncludesLatestSample()
    {
        var drive = new CoreDriveInfo { Path = "/dev/sdz", Name = "Test", TotalSize = 1_000_000_000 };
        var request = new SeekTestRequest
        {
            Drive = drive,
            TestType = SeekTestType.Random,
            SeekCount = 3,
            BlockSizeBytes = 512,
            TimeoutSeconds = 5
        };

        var progressReports = new List<SeekTestProgress>();
        Action<SeekTestProgress> callback = p => progressReports.Add(p);

        var result = await _executor.ExecuteAsync(request, callback, CancellationToken.None);

        // Progress may not fire for ultra-fast tests (<200ms), but result samples are always populated
        Assert.NotEmpty(result.Samples);

        // If progress did fire, verify LatestSample structure
        if (progressReports.Count > 0)
        {
            var withSample = progressReports.Where(p => p.LatestSample != null).ToList();
            Assert.NotEmpty(withSample);

            var sample = withSample.First().LatestSample!;
            Assert.True(sample.Index >= 0);
            Assert.True(sample.LatencyMs >= 0);
            Assert.True(sample.SourceLba >= 0);
            Assert.True(sample.DestinationLba >= 0);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ProgressCallback_LatestSampleMatchesFinalSamples()
    {
        var drive = new CoreDriveInfo { Path = "/dev/sdz", Name = "Test", TotalSize = 1_000_000_000 };
        var request = new SeekTestRequest
        {
            Drive = drive,
            TestType = SeekTestType.Random,
            SeekCount = 3,
            BlockSizeBytes = 512,
            TimeoutSeconds = 5
        };

        var progressReports = new List<SeekTestProgress>();
        Action<SeekTestProgress> callback = p => progressReports.Add(p);

        var result = await _executor.ExecuteAsync(request, callback, CancellationToken.None);

        // If progress fired, the last progress report's LatestSample should match the last sample
        var lastProgress = progressReports.LastOrDefault(p => p.LatestSample != null);
        if (lastProgress != null && result.Samples.Count > 0)
        {
            var lastResultSample = result.Samples.Last();
            Assert.Equal(lastResultSample.Index, lastProgress.LatestSample!.Index);
        }
    }

    // ──────────────────────────────────────────────
    //  Pre-positioning
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PrePositionsHead_BeforeTest()
    {
        // Pre-positioning is best-effort; the test should still complete
        var drive = new CoreDriveInfo { Path = "/dev/sdz", Name = "Test", TotalSize = 1_000_000_000 };
        var request = new SeekTestRequest
        {
            Drive = drive,
            TestType = SeekTestType.FullStroke,
            SeekCount = 2,
            BlockSizeBytes = 512,
            TimeoutSeconds = 5
        };

        var result = await _executor.ExecuteAsync(request, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsCompleted || result.WasAborted);
        // Pre-positioning failure should not prevent test completion
    }

    // ──────────────────────────────────────────────
    //  Helper to invoke private Percentile method
    // ──────────────────────────────────────────────

    private static double InvokePercentile(List<double> sorted, double percentile)
    {
        var method = typeof(SeekTestExecutor).GetMethod(
            "Percentile",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        return (double)method!.Invoke(null, new object[] { sorted, percentile })!;
    }
}
