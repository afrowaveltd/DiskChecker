using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Factory for creating appropriate surface test executors based on the request.
/// </summary>
public class SurfaceTestExecutorFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public SurfaceTestExecutorFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Creates an appropriate surface test executor for the given request.
    /// </summary>
    /// <param name="request">Surface test request.</param>
    /// <returns>Surface test executor.</returns>
    public ISurfaceTestExecutor Create(SurfaceTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Return appropriate executor based on test type or platform
        // For now, return the default surface test executor
        return new SurfaceTestExecutor(_loggerFactory.CreateLogger<SurfaceTestExecutor>());
    }
}