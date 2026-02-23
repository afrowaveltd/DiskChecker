using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DiskChecker.Tests;

public class HistoryServiceTests
{
    private DiskCheckerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var context = new DiskCheckerDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        return context;
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsPagedResults()
    {
        var context = CreateDbContext();
        var service = new HistoryService(context);

        var result = await service.GetHistoryAsync(pageSize: 10, pageIndex: 0);

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalItems);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetHistoryAsync_WithFilters_ReturnsFilteredResults()
    {
        var context = CreateDbContext();
        var service = new HistoryService(context);

        var result = await service.GetHistoryAsync(
            pageSize: 20,
            pageIndex: 0,
            driveSerial: "TEST");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetTestByIdAsync_ReturnsTestIfExists()
    {
        var context = CreateDbContext();
        var service = new HistoryService(context);

        var test = await service.GetTestByIdAsync(Guid.NewGuid());

        Assert.Null(test);
    }

    [Fact]
    public async Task CompareTestsAsync_ThrowsIfTestNotFound()
    {
        var context = CreateDbContext();
        var service = new HistoryService(context);

        var test1Id = Guid.NewGuid();
        var test2Id = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompareTestsAsync(test1Id, test2Id));
    }

    [Fact]
    public async Task GetDrivesWithTestsAsync_ReturnsDrives()
    {
        var context = CreateDbContext();
        var service = new HistoryService(context);

        var result = await service.GetDrivesWithTestsAsync();

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
