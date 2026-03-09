using DiskChecker.Core.Extensions;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware
{
    public class WindowsSmartaProvider : ISmartaProvider
    {
        private readonly ILogger<WindowsSmartaProvider> _logger;

        public WindowsSmartaProvider(ILogger<WindowsSmartaProvider>? logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SmartaData?> GetSmartaDataAsync(string devicePath)
        {
            try
            {
                // Proper implementation with ToSafeString usage
                var safeDevicePath = devicePath.ToSafeString();
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Getting SMART data for device: {DevicePath}", safeDevicePath);
                }
                
                // Implementation would go here
                var smartaData = new SmartaData
                {
                    DeviceModel = "Windows Drive".ToSafeString(),
                    SerialNumber = "SN789012".ToSafeString(),
                    Temperature = 42 // example value
                };
                
                return await Task.FromResult(smartaData);
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(ex, "Error getting SMART data for device path");
                }
                return null;
            }
        }

        public Task<bool> IsDriveValidAsync(string devicePath)
        {
            // Safe usage of ToSafeString
            var safePath = devicePath.ToSafeString();
            // Implementation would go here
            return Task.FromResult(!string.IsNullOrEmpty(safePath));
        }

        public Task<List<string>> ListDrivesAsync()
        {
            // Implementation would go here
            var drives = new List<string> { @"\\.\PHYSICALDRIVE0", @"\\.\PHYSICALDRIVE1" };
            return Task.FromResult(drives.Select(d => d.ToSafeString()).ToList());
        }

        public Task<string> GetDependencyInstructionsAsync()
        {
            return Task.FromResult("Install smartctl for Windows".ToSafeString());
        }

        public Task<bool> TryInstallDependenciesAsync()
        {
            // Implementation would go here
            return Task.FromResult(true);
        }

        public Task<int?> GetTemperatureOnlyAsync(string devicePath)
        {
            // Safe usage of ToSafeString
            var safePath = devicePath.ToSafeString();
            // Implementation would go here
            return Task.FromResult<int?>(42);
        }
    }
}