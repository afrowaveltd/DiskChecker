using System.Runtime.InteropServices;
using DiskChecker.Core.Interfaces;
using DiskChecker.Infrastructure.Hardware;

namespace DiskChecker.Infrastructure.Persistence;

public class SmartaProviderFactory
{
    public ISmartaProvider Create()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? new LinuxSmartaProvider()
            : new WindowsSmartaProvider();
    }
}
