using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware;

namespace DiskChecker.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DiskCheckerDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<DiskCheckerDbContext>();

        services.AddSingleton<SmartaProviderFactory>();
        services.AddScoped<ISmartaProvider>(sp =>
            sp.GetRequiredService<SmartaProviderFactory>().Create());

        return services;
    }
}
