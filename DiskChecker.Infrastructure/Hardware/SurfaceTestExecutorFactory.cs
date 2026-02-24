using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Factory for creating appropriate surface test executor based on profile and platform.
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
    /// </summary>
    /// <param name="request">The surface test request with profile information.</param>
    /// <returns>An appropriate ISurfaceTestExecutor implementation.</returns>
    public ISurfaceTestExecutor Create(SurfaceTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        
        // Use sequential file executor for full disk sanitization on Windows
        if (request.Profile == SurfaceTestProfile.FullDiskSanitization)
        {
            return new SequentialFileTestExecutor(_smartaProvider);
        }
        
        // Default to standard executor for other profiles
        return new SurfaceTestExecutor(_smartaProvider);
    }
}
