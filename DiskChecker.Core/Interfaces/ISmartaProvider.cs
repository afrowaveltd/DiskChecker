
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces
{
    public interface ISmartaProvider
    {
        Task<SmartaData?> GetSmartaDataAsync(string devicePath);
        Task<bool> IsDriveValidAsync(string devicePath);
        Task<List<string>> ListDrivesAsync();
        Task<string> GetDependencyInstructionsAsync();
        Task<bool> TryInstallDependenciesAsync();
        Task<int?> GetTemperatureOnlyAsync(string devicePath);
    }
}
