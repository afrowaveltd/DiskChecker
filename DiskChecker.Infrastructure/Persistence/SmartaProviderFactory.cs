using System.Runtime.InteropServices;
using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware;

namespace DiskChecker.Infrastructure.Persistence;

public static class SmartaProviderFactory
{
    public static ISmartaProvider CreateProvider()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? new LinuxSmartaProvider()
            : new WindowsSmartaProvider();
    }
}
