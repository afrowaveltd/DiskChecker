using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Factory for creating appropriate surface test executor based on profile and platform.
/// Prioritizes low-level disk access (no OS buffering) for accuracy.
/// </summary>
public class SurfaceTestExecutorFactory
{
    private readonly ISmartaProvider _smartaProvider;

    public SurfaceTestExecutorFactory(ISmartaProvider smartaProvider)
    {
        ArgumentNullException.ThrowIfNull(smartaProvider);
        _smartaProvider = smartaProvider;
    }

    /// <summary>
    /// Creates a surface test executor appropriate for the given request.
    /// Uses low-level disk access (no OS buffering) by default for accurate testing.
    /// </summary>
    /// <param name="request">The surface test request with profile information.</param>
    /// <returns>An appropriate ISurfaceTestExecutor implementation.</returns>
    public ISurfaceTestExecutor Create(SurfaceTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        
        // Use low-level disk surface test for all profiles (no OS buffering)
        // This ensures accurate throughput measurements and complete surface verification
        return new DiskSurfaceTestExecutor(_smartaProvider);
    }
}
