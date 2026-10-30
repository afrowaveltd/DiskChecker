using System;
using System.Threading.Tasks;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiskChecker.Tests;

public class DiskCardIdentityPersistenceTests
{
    [Fact]
    public async Task CreateAsync_SameDevicePathWithDifferentSerials_KeepsBothCards()
    {
        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseInMemoryDatabase("DiskPathCollision_" + Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new DiskCheckerDbContext(options);
        var repository = new DiskCardRepository(db);
        var first = await repository.CreateAsync(new DiskCard
        {
            ModelName = "Hitachi",
            SerialNumber = "HITACHI-12345",
            DevicePath = @"\\.\PhysicalDrive2",
            Capacity = 500_000_000_000L
        });

        var second = await repository.CreateAsync(new DiskCard
        {
            ModelName = "WesternDigital",
            SerialNumber = "WD-98765",
            DevicePath = @"\\.\PhysicalDrive2",
            Capacity = 1_000_000_000_000L
        });

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await db.DiskCards.CountAsync(TestContext.Current.CancellationToken));
        var persistedFirst = await db.DiskCards.FindAsync(
            new object?[] { first.Id }, TestContext.Current.CancellationToken);
        var persistedSecond = await db.DiskCards.FindAsync(
            new object?[] { second.Id }, TestContext.Current.CancellationToken);
        Assert.NotNull(persistedFirst);
        Assert.NotNull(persistedSecond);
        Assert.Equal("Hitachi", persistedFirst.ModelName);
        Assert.Equal(500_000_000_000L, persistedFirst.Capacity);
        Assert.Equal("WesternDigital", persistedSecond.ModelName);
    }
}
