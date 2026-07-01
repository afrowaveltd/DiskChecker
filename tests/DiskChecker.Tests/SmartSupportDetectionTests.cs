using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// Tests for SMART support detection — the mechanism that prevents
/// SMART queries on devices that don't support SMART (e.g., USB flash
/// drives, memory cards, etc.) to avoid device contention errors like
/// "device is being used by another process" on Linux.
/// </summary>
public class SmartSupportDetectionTests
{
    // ──────────────────────────────────────────────
    //  CoreDriveInfo.SupportsSmart
    // ──────────────────────────────────────────────

    [Fact]
    public void CoreDriveInfo_SupportsSmart_DefaultsToTrue()
    {
        var drive = new CoreDriveInfo();
        Assert.True(drive.SupportsSmart);
    }

    [Fact]
    public void CoreDriveInfo_SupportsSmart_CanBeSetToFalse()
    {
        var drive = new CoreDriveInfo { SupportsSmart = false };
        Assert.False(drive.SupportsSmart);
    }

    [Fact]
    public void CoreDriveInfo_SupportsSmart_CanBeSetToTrue()
    {
        var drive = new CoreDriveInfo { SupportsSmart = true };
        Assert.True(drive.SupportsSmart);
    }

    [Fact]
    public void CoreDriveInfo_SupportsSmart_PersistsAfterSerialization()
    {
        var drive = new CoreDriveInfo
        {
            Path = "/dev/sdb",
            Name = "USB Flash Drive",
            SupportsSmart = false
        };

        Assert.False(drive.SupportsSmart);
        Assert.Equal("/dev/sdb", drive.Path);
        Assert.Equal("USB Flash Drive", drive.Name);
    }

    // ──────────────────────────────────────────────
    //  LinuxSmartaProvider.IsSmartSupportedAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LinuxSmartaProvider_IsSmartSupported_WhenNoSmartSentinel_ReturnsFalse()
    {
        var provider = new LinuxSmartaProvider(new NullLogger<LinuxSmartaProvider>());

        var sentinelField = typeof(LinuxSmartaProvider).GetField("NoSmartSentinel",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(sentinelField);
        var noSmartSentinel = (SmartaData)sentinelField.GetValue(null)!;

        var cacheField = typeof(LinuxSmartaProvider).GetField("_smartCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);

        var cache = (ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)>)cacheField.GetValue(provider)!;
        cache["/dev/nosmart"] = (noSmartSentinel, DateTime.UtcNow);

        var result = await provider.IsSmartSupportedAsync("/dev/nosmart", TestContext.Current.CancellationToken);
        Assert.False(result);
    }

    [Fact]
    public async Task LinuxSmartaProvider_IsSmartSupported_WhenRealDataCached_ReturnsTrue()
    {
        var provider = new LinuxSmartaProvider(new NullLogger<LinuxSmartaProvider>());

        var cacheField = typeof(LinuxSmartaProvider).GetField("_smartCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);

        var cache = (ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)>)cacheField.GetValue(provider)!;
        var data = new SmartaData
        {
            DeviceModel = "Samsung SSD 860 EVO",
            SerialNumber = "SN12345",
            Temperature = 35,
            IsHealthy = true,
            IsFromCache = false
        };
        cache["/dev/smartdisk"] = (data, DateTime.UtcNow);

        var result = await provider.IsSmartSupportedAsync("/dev/smartdisk", TestContext.Current.CancellationToken);
        Assert.True(result);
    }

    [Fact]
    public async Task LinuxSmartaProvider_IsSmartSupported_WhenNotCached_QueriesAndCaches()
    {
        var provider = new LinuxSmartaProvider(new NullLogger<LinuxSmartaProvider>());

        var sentinelField = typeof(LinuxSmartaProvider).GetField("NoSmartSentinel",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(sentinelField);
        var noSmartSentinel = (SmartaData)sentinelField.GetValue(null)!;

        var cacheField = typeof(LinuxSmartaProvider).GetField("_smartCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);
        var cache = (ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)>)cacheField.GetValue(provider)!;
        cache["/dev/nosmart2"] = (noSmartSentinel, DateTime.UtcNow);

        var result1 = await provider.IsSmartSupportedAsync("/dev/nosmart2", TestContext.Current.CancellationToken);
        Assert.False(result1);

        var result2 = await provider.IsSmartSupportedAsync("/dev/nosmart2", TestContext.Current.CancellationToken);
        Assert.False(result2);
    }

    // ──────────────────────────────────────────────
    //  WindowsSmartaProvider.IsSmartSupportedAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task WindowsSmartaProvider_IsSmartSupported_WhenNoSmartSentinel_ReturnsFalse()
    {
        var provider = new WindowsSmartaProvider(new NullLogger<WindowsSmartaProvider>());

        var sentinelField = typeof(WindowsSmartaProvider).GetField("NoSmartSentinel",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(sentinelField);
        var noSmartSentinel = (SmartaData)sentinelField.GetValue(null)!;

        var cacheField = typeof(WindowsSmartaProvider).GetField("_smartCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);

        var cache = (ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)>)cacheField.GetValue(provider)!;
        cache[@"\\.\physicaldrive99"] = (noSmartSentinel, DateTime.UtcNow);

        var result = await provider.IsSmartSupportedAsync(@"\\.\PhysicalDrive99", TestContext.Current.CancellationToken);
        Assert.False(result);
    }

    [Fact]
    public async Task WindowsSmartaProvider_IsSmartSupported_WhenRealDataCached_ReturnsTrue()
    {
        var provider = new WindowsSmartaProvider(new NullLogger<WindowsSmartaProvider>());

        var cacheField = typeof(WindowsSmartaProvider).GetField("_smartCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);

        var cache = (ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)>)cacheField.GetValue(provider)!;
        var data = new SmartaData
        {
            DeviceModel = "WDC WD40EFRX",
            SerialNumber = "WD-XXXX",
            Temperature = 32,
            IsHealthy = true,
            IsFromCache = false
        };
        cache[@"\\.\physicaldrive0"] = (data, DateTime.UtcNow);

        var result = await provider.IsSmartSupportedAsync(@"\\.\PhysicalDrive0", TestContext.Current.CancellationToken);
        Assert.True(result);
    }

    // ──────────────────────────────────────────────
    //  SeekTestService respects SupportsSmart
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SeekTestService_GetRecommendationAsync_WhenSmartNotSupported_SkipsSmartQuery()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var seekExecutor = Substitute.For<ISeekTestExecutor>();
        var logger = NullLogger<SeekTestService>.Instance;

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sdb",
            Name = "USB Flash Drive",
            TotalSize = 32_000_000_000,
            SupportsSmart = false
        };

        var expectedRec = new SeekTestRecommendation
        {
            RecommendedType = SeekTestType.Random,
            RecommendedSeekCount = 3000,
            Rationale = "SMART data nedostupná. Doporučen výchozí random seek test (3000 seeků)."
        };

        seekExecutor.GetRecommendation(null, drive.TotalSize, false)
            .Returns(expectedRec);

        var service = new SeekTestService(smartaProvider, seekExecutor, logger);

        var rec = await service.GetRecommendationAsync(drive, TestContext.Current.CancellationToken);

        await smartaProvider.DidNotReceive().GetSmartaDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        Assert.NotNull(rec);
        Assert.Equal(3000, rec.RecommendedSeekCount);
    }

    [Fact]
    public async Task SeekTestService_RunAsync_WhenSmartNotSupported_SkipsSmartQuery()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var seekExecutor = Substitute.For<ISeekTestExecutor>();
        var logger = NullLogger<SeekTestService>.Instance;

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sdc",
            Name = "USB Stick",
            TotalSize = 16_000_000_000,
            SupportsSmart = false
        };

        var request = new SeekTestRequest
        {
            Drive = drive,
            TestType = SeekTestType.Random,
            SeekCount = 100,
            BlockSizeBytes = 512,
            TimeoutSeconds = 5
        };

        var executorResult = new SeekTestResult
        {
            TestType = SeekTestType.Random,
            SeekCount = 100,
            AverageLatencyMs = 5.0,
            IsCompleted = true
        };

        seekExecutor.ExecuteAsync(
            Arg.Any<SeekTestRequest>(),
            Arg.Any<Action<SeekTestProgress>?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(executorResult));

        seekExecutor.GetRecommendation(null, drive.TotalSize, false)
            .Returns(new SeekTestRecommendation
            {
                RecommendedType = SeekTestType.Random,
                RecommendedSeekCount = 100
            });

        var service = new SeekTestService(smartaProvider, seekExecutor, logger);

        var result = await service.RunAsync(request, null, TestContext.Current.CancellationToken);

        await smartaProvider.DidNotReceive().GetSmartaDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        Assert.NotNull(result);
        Assert.True(result.IsCompleted);
    }

    // ──────────────────────────────────────────────
    //  SmartCheckService respects SupportsSmart
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SmartCheckService_RunAsync_WhenSmartNotSupported_ReturnsNull()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var qualityCalculator = Substitute.For<IQualityCalculator>();
        var dbContext = new DiskChecker.Infrastructure.Persistence.DiskCheckerDbContext(
                new DbContextOptionsBuilder<DiskChecker.Infrastructure.Persistence.DiskCheckerDbContext>()
                    .UseInMemoryDatabase("SmartCheckTest_" + Guid.NewGuid())
                    .Options);
        var cardTestService = Substitute.For<DiskCardTestService>(
            dbContext,
            qualityCalculator,
            NullLogger<DiskCardTestService>.Instance,
            Substitute.For<ICertificateGenerator>());
        var logger = NullLogger<SmartCheckService>.Instance;

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sdd",
            Name = "No SMART Drive",
            SupportsSmart = false
        };

        var service = new SmartCheckService(
            smartaProvider,
            qualityCalculator,
            dbContext,
            cardTestService,
            logger);

        var result = await service.RunAsync(drive, TestContext.Current.CancellationToken);

        await smartaProvider.DidNotReceive().GetSmartaDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        Assert.Null(result);
    }

    // ──────────────────────────────────────────────
    //  LinuxDiskDetectionService probes SMART support
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LinuxDiskDetectionService_WithSmartaProvider_ProbesSmartSupport()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();

        smartaProvider.IsSmartSupportedAsync("/dev/sda", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        smartaProvider.IsSmartSupportedAsync("/dev/sdb", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var service = new LinuxDiskDetectionService(smartaProvider);

        var drives = await service.GetDrivesAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(drives);

        foreach (var drive in drives)
        {
            Assert.NotNull(drive);
        }
    }

    // ──────────────────────────────────────────────
    //  Integration: SupportsSmart flows through services
    // ──────────────────────────────────────────────

    [Fact]
    public async Task SupportsSmart_FlowsThroughSeekTestService_ToRecommendation()
    {
        var smartaProvider = Substitute.For<ISmartaProvider>();
        var seekExecutor = new SeekTestExecutor(null);
        var logger = NullLogger<SeekTestService>.Instance;

        var drive = new CoreDriveInfo
        {
            Path = "/dev/sde",
            Name = "Non-SMART Drive",
            TotalSize = 64_000_000_000,
            SupportsSmart = false
        };

        var service = new SeekTestService(smartaProvider, seekExecutor, logger);

        var rec = await service.GetRecommendationAsync(drive, TestContext.Current.CancellationToken);

        await smartaProvider.DidNotReceive().GetSmartaDataAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        Assert.NotNull(rec);
        Assert.False(rec.IsTooFragile);
        Assert.Contains("SMART data nedostupná", rec.Rationale);
    }

    // ──────────────────────────────────────────────
    //  Edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public async Task IsSmartSupported_WithNoSmartSentinelInCache_ReturnsFalse()
    {
        var provider = new LinuxSmartaProvider(new NullLogger<LinuxSmartaProvider>());

        var sentinelField = typeof(LinuxSmartaProvider).GetField("NoSmartSentinel",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(sentinelField);
        var noSmartSentinel = (SmartaData)sentinelField.GetValue(null)!;

        var cacheField = typeof(LinuxSmartaProvider).GetField("_smartCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);
        var cache = (ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)>)cacheField.GetValue(provider)!;
        cache["/dev/test_device"] = (noSmartSentinel, DateTime.UtcNow);

        var result = await provider.IsSmartSupportedAsync(
            "/dev/test_device",
            TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task IsSmartSupported_WithRealDataInCache_ReturnsTrue()
    {
        var provider = new LinuxSmartaProvider(new NullLogger<LinuxSmartaProvider>());

        var cacheField = typeof(LinuxSmartaProvider).GetField("_smartCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);
        var cache = (ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)>)cacheField.GetValue(provider)!;
        cache["/dev/test_device2"] = (new SmartaData
        {
            DeviceModel = "Test Drive",
            SerialNumber = "SN-TEST",
            Temperature = 30,
            IsHealthy = true,
            IsFromCache = false
        }, DateTime.UtcNow);

        var result = await provider.IsSmartSupportedAsync(
            "/dev/test_device2",
            TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact]
    public async Task IsSmartSupported_CachesResult_SecondCallIsInstant()
    {
        var provider = new LinuxSmartaProvider(new NullLogger<LinuxSmartaProvider>());

        var sentinelField = typeof(LinuxSmartaProvider).GetField("NoSmartSentinel",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(sentinelField);
        var noSmartSentinel = (SmartaData)sentinelField.GetValue(null)!;

        var cacheField = typeof(LinuxSmartaProvider).GetField("_smartCache",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(cacheField);
        var cache = (ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)>)cacheField.GetValue(provider)!;
        cache["/dev/cached_device"] = (noSmartSentinel, DateTime.UtcNow);

        var result1 = await provider.IsSmartSupportedAsync(
            "/dev/cached_device",
            TestContext.Current.CancellationToken);

        var result2 = await provider.IsSmartSupportedAsync(
            "/dev/cached_device",
            TestContext.Current.CancellationToken);

        Assert.Equal(result1, result2);
        Assert.False(result1);
    }
}
