using Microsoft.Extensions.DependencyInjection;

namespace DiskChecker.Core;

/// <summary>
/// Extension methods for registering core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core DiskChecker services to the service collection.
    /// </summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // Register core services
        services.AddSingleton<Interfaces.IQualityCalculator, Services.QualityCalculator>();
        
        return services;
    }
}