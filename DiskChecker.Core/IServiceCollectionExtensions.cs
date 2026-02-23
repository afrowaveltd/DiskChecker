using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Services;

namespace DiskChecker.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<IQualityCalculator, QualityCalculator>();

        return services;
    }
}
