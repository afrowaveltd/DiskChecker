using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiskChecker.Tests;

public class EmailSettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoadSettings_PersistsValues()
    {
        using var dbContext = CreateDbContext();
        var defaults = Options.Create(new EmailSettings { Host = "smtp", FromAddress = "from@test" });
        var service = new EmailSettingsService(dbContext, defaults);

        var settings = new EmailSettings
        {
            Host = "smtp.test",
            Port = 2525,
            UseSsl = false,
            UserName = "user",
            Password = "pass",
            FromName = "DiskChecker",
            FromAddress = "sender@test"
        };

        await service.SaveAsync(settings);
        var loaded = await service.GetAsync();

        Assert.Equal(settings.Host, loaded.Host);
        Assert.Equal(settings.Port, loaded.Port);
        Assert.Equal(settings.FromAddress, loaded.FromAddress);
    }

    private static DiskCheckerDbContext CreateDbContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<DiskCheckerDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new DiskCheckerDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
