using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware;
using DiskChecker.Infrastructure.Hardware.Sanitization;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Infrastructure.Services;

namespace DiskChecker.Infrastructure;

public static class InfrastructureServiceExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        string connectionString)
    {
        // Database context is configured in App.axaml.cs with proper DbContextOptions
        // Do not register DbContext as Singleton here - it's configured with UseSqlite

        // Services (Windows-only due to System.Drawing)
        services.AddScoped<CertificateGenerator>();
        services.AddScoped<IMetricsCollector>(provider => provider.GetRequiredService<MetricsCollector>());
        
        // Interfaces
        services.AddScoped<ICertificateGenerator, CertificateGenerator>();

        return services;
    }

    public static IServiceCollection AddPersistenceServices(
        this IServiceCollection services,
        string dataDirectory)
    {
        // Ensure data directory exists
        System.IO.Directory.CreateDirectory(dataDirectory);

        // Configure DbContext with SQLite
        var dbPath = System.IO.Path.Combine(dataDirectory, "DiskChecker.db");
        services.AddDbContext<DiskCheckerDbContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath}");
        });

        services.AddScoped<IDiskCardRepository, DiskCardRepository>();
        
        return services;
    }
}