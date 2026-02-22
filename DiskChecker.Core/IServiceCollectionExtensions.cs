using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
        });

        return services;
    }
}
