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

        /// <summary>
        /// Indicates whether the last SMART operation failed due to insufficient permissions
        /// (e.g., not running as root on Linux). Resets on each new operation.
        /// </summary>
        bool LastOperationWasPermissionDenied { get; }

        /// <summary>
        /// Checks whether the given device supports SMART monitoring.
        /// Returns false for devices that don't support SMART (e.g., USB flash drives,
        /// memory cards, NVMe drives behind certain bridges, etc.).
        /// The result is cached so subsequent calls are instant.
        /// </summary>
        Task<bool> IsSmartSupportedAsync(string devicePath, CancellationToken cancellationToken = default);
    }
}