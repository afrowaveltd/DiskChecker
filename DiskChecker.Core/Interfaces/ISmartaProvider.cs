using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces
{
    public interface ISmartaProvider
    {
        Task<SmartaData?> GetSmartaDataAsync(string devicePath, CancellationToken cancellationToken = default);
        Task<bool> IsDriveValidAsync(string devicePath, CancellationToken cancellationToken = default);
        Task<List<string>> ListDrivesAsync(CancellationToken cancellationToken = default);
        Task<string> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default);
        Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default);
        Task<int?> GetTemperatureOnlyAsync(string devicePath, CancellationToken cancellationToken = default);
    }
}