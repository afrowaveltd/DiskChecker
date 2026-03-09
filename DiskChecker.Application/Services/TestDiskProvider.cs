
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services
{
    public class TestDiskProvider : ISmartaProvider
    {
        public Task<SmartaData?> GetSmartaDataAsync(string devicePath)
        {
            throw new NotImplementedException();
        }

        public Task<bool> IsDriveValidAsync(string devicePath)
        {
            throw new NotImplementedException();
        }

        public Task<List<string>> ListDrivesAsync()
        {
            throw new NotImplementedException();
        }

        public Task<string> GetDependencyInstructionsAsync()
        {
            throw new NotImplementedException();
        }

        public Task<bool> TryInstallDependenciesAsync()
        {
            throw new NotImplementedException();
        }

        public Task<int?> GetTemperatureOnlyAsync(string devicePath)
        {
            throw new NotImplementedException();
        }
    }
}
