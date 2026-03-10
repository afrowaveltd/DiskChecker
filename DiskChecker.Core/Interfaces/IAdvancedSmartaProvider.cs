using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces
{
    public interface IAdvancedSmartaProvider : ISmartaProvider
    {
        Task<SmartaData?> GetAdvancedSmartaDataAsync(string devicePath, CancellationToken cancellationToken = default);
        Task<List<SmartaAttributeItem>> GetSmartAttributesAsync(string devicePath, CancellationToken cancellationToken = default);
        Task<SmartaSelfTestStatus> GetSelfTestStatusAsync(string devicePath, CancellationToken cancellationToken = default);
        Task<List<SmartaSelfTestEntry>> GetSelfTestLogAsync(string devicePath, CancellationToken cancellationToken = default);
        Task<List<SmartaMaintenanceAction>> GetSupportedMaintenanceActionsAsync(string devicePath, CancellationToken cancellationToken = default);
        Task<bool> ExecuteMaintenanceActionAsync(string devicePath, SmartaMaintenanceAction action, CancellationToken cancellationToken = default);
        Task<bool> StartSelfTestAsync(string devicePath, SmartaSelfTestType testType, CancellationToken cancellationToken = default);
        // Runtime configuration
        Task SetCacheTtlMinutesAsync(int minutes, CancellationToken cancellationToken = default);
        // Cache management and telemetry
        Task ClearSmartCacheAsync(CancellationToken cancellationToken = default);
        Task RemoveSmartCacheForDeviceAsync(string devicePath, CancellationToken cancellationToken = default);
        Task RemoveSmartCacheForSerialAsync(string serialNumber, CancellationToken cancellationToken = default);
        Task<(int Hits, int Misses, int Items)> GetSmartCacheStatsAsync(CancellationToken cancellationToken = default);
    }
}