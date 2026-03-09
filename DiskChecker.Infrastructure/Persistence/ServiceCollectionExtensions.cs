using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<SmartaProviderFactory>();
        services.AddScoped<ISmartaProvider>(provider => provider.GetRequiredService<SmartaProviderFactory>().Create());
        return services;
    }
}