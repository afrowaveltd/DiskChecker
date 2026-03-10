using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services;

/// <summary>
/// Test implementation of ISmartaProvider for unit testing.
/// </summary>
public class TestDiskProvider : ISmartaProvider
{
    public Task<SmartaData?> GetSmartaDataAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> IsDriveValidAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<List<string>> ListDrivesAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<string> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<int?> GetTemperatureOnlyAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}