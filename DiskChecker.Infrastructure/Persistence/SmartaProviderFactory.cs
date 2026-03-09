using System.Runtime.InteropServices;
using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Persistence;

public class SmartaProviderFactory
{
    private readonly ILoggerFactory? _loggerFactory;

    public SmartaProviderFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
    }

    public ISmartaProvider Create()
    {
        var logger = _loggerFactory?.CreateLogger<WindowsSmartaProvider>();
        var linuxLogger = _loggerFactory?.CreateLogger<LinuxSmartaProvider>();
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? new LinuxSmartaProvider(linuxLogger)
            : new WindowsSmartaProvider(logger);
    }
}