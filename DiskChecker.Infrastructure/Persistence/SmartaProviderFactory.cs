using System.Runtime.InteropServices;
using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// Factory for creating platform-specific SMART data providers.
/// </summary>
public class SmartaProviderFactory
{
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmartaProviderFactory"/> class.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for diagnostics.</param>
    public SmartaProviderFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Creates a platform-specific SMART provider.
    /// </summary>
    /// <returns>A platform-appropriate SMART provider instance.</returns>
    public ISmartaProvider Create()
    {
        var logger = _loggerFactory?.CreateLogger<WindowsSmartaProvider>();
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? new LinuxSmartaProvider()
            : new WindowsSmartaProvider(logger);
    }
}
