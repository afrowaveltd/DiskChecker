using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DiskChecker.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DiskCheckerDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<DiskCheckerDbContext>();

        return services;
    }
}
