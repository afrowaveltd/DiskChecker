using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services;

public class TestDiskProvider : ISmartaProvider
{
    public Task<SmartaData?> GetSmartaDataAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<SmartaData?>(null);
    }

    public Task<bool> IsDriveValidAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<CoreDriveInfo>> ListDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<CoreDriveInfo>
        {
            new CoreDriveInfo { Path = @"\\.\PhysicalDrive0", Name = "Windows Disk", TotalSize = 512000000000L },
            new CoreDriveInfo { Path = @"\\.\PhysicalDrive1", Name = "Data Disk", TotalSize = 1000000000000L }
        };
        return Task.FromResult<IReadOnlyList<CoreDriveInfo>>(drives);
    }

    public Task<string?> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
