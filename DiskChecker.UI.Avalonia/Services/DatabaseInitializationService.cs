using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DiskChecker.Infrastructure.Persistence;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Hosted service that ensures database is created on application startup.
/// </summary>
public class DatabaseInitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public DatabaseInitializationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DiskCheckerDbContext>();
        
        // Ensure database is created
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        
        // Apply any pending migrations (if using migrations)
        // await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}