using System;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests;

/// <summary>
/// A SMART check can be taken through an IDE-to-USB2 bridge and used for a
/// later surface test through an IDE-to-USB3 bridge with no SMART passthrough.
/// Capacity is only an eligibility filter; the UI must obtain human consent.
/// </summary>
public class SmartAdapterHandoffTests
{
    private static DiskCheckerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new DiskCheckerDbContext(options);
    }

    private static DiskCardTestService CreateService(DiskCheckerDbContext db)
    {
        return new DiskCardTestService(
            db,
            Substitute.For<IQualityCalculator>(),
            Substitute.For<ILogger<DiskCardTestService>>(),
            Substitute.For<ICertificateGenerator>());
    }

    private static async Task<TestSession> SeedSmartAsync(DiskCheckerDbContext db, long size, string serial,
        TestType type = TestType.SmartShort, DateTime? completed = null)
    {
        var card = new DiskCard
        {
            ModelName = "IDE disk on USB2",
            SerialNumber = serial,
            DevicePath = "/dev/sdz",
            Capacity = size
        };
        db.DiskCards.Add(card);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var s = new TestSession
        {
            DiskCardId = card.Id,
            StartedAt = (completed ?? DateTime.UtcNow).AddMinutes(-1),
            CompletedAt = completed ?? DateTime.UtcNow,
            Status = TestStatus.Completed,
            TestType = type,
            SmartBefore = new SmartaData
            {
                DeviceModel = "IDE disk",
                SerialNumber = serial,
                TotalSize = size,
                IsHealthy = true,
                Temperature = 31
            }
        };
        db.TestSessions.Add(s);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return s;
    }

    [Fact]
    public async Task SameCapacityWithoutSmartOnNewAdapter_OffersPreviousCheck()
    {
        await using var db = CreateContext();
        var saved = await SeedSmartAsync(db, 160_041_885_696, "REAL-SN-1234");
        var service = CreateService(db);
        var candidate = await service.FindLatestReusableSmartAsync(
            new CoreDriveInfo { Name = "USB3 bridge", Path = "/dev/sdx",
                SupportsSmart = false, TotalSize = 160_041_885_696 },
            TestContext.Current.CancellationToken);

        Assert.NotNull(candidate);
        Assert.Equal(saved.Id, candidate.SourceSessionId);
        Assert.Equal("REAL-SN-1234", candidate.Data.SerialNumber);
        Assert.Equal(saved.DiskCardId, candidate.SourceDiskCardId);
    }

    [Fact]
    public async Task SameRoundedGbButDifferentByteSize_MustNotMatch()
    {
        await using var db = CreateContext();
        await SeedSmartAsync(db, 160_041_885_696, "REAL-SN-1234");
        var candidate = await CreateService(db).FindLatestReusableSmartAsync(
            new CoreDriveInfo { TotalSize = 160_041_885_184 },
            TestContext.Current.CancellationToken);
        Assert.Null(candidate);
    }

    [Fact]
    public async Task ConflictingReliableSerials_MustNotMatchEvenWithIdenticalSize()
    {
        await using var db = CreateContext();
        await SeedSmartAsync(db, 160_041_885_696, "REAL-SN-1234");
        var candidate = await CreateService(db).FindLatestReusableSmartAsync(
            new CoreDriveInfo { TotalSize = 160_041_885_696, SerialNumber = "DIFFERENT-SN-789" },
            TestContext.Current.CancellationToken);
        Assert.Null(candidate);
    }

    [Fact]
    public async Task ConflictingBridgeSerial_RequiresExplicitWarning_NotAutomaticRejection()
    {
        await using var db = CreateContext();
        await SeedSmartAsync(db, 160_041_885_696, "REAL-SN-1234");
        var candidate = await CreateService(db).FindLatestReusableSmartAsync(
            new CoreDriveInfo { TotalSize = 160_041_885_696,
                SerialNumber = "USB-BRIDGE-SERIAL-456", SupportsSmart = false },
            TestContext.Current.CancellationToken);
        Assert.NotNull(candidate);
        Assert.True(candidate.ConflictingSerial);
    }

    [Fact]
    public async Task OldSmartBeyondThirtyDays_NotOfferedAsRecent()
    {
        await using var db = CreateContext();
        await SeedSmartAsync(db, 160_041_885_696, "REAL-SN-1234",
            completed: DateTime.UtcNow.AddDays(-35));
        var candidate = await CreateService(db).FindLatestReusableSmartAsync(
            new CoreDriveInfo { TotalSize = 160_041_885_696 },
            TestContext.Current.CancellationToken);
        Assert.Null(candidate);
    }

    [Fact]
    public async Task OnlySmartChecks_NotSnapshotsBorrowedIntoSurfaceTests()
    {
        await using var db = CreateContext();
        await SeedSmartAsync(db, 160_041_885_696, "REAL-SN-1234", TestType.QuickRead);
        var candidate = await CreateService(db).FindLatestReusableSmartAsync(
            new CoreDriveInfo { TotalSize = 160_041_885_696 },
            TestContext.Current.CancellationToken);
        Assert.Null(candidate);
    }
}
