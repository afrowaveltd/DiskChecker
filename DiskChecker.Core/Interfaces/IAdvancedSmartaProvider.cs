
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces
{
    public interface IAdvancedSmartaProvider : ISmartaProvider
    {
        Task<SmartaData?> GetAdvancedSmartaDataAsync(string devicePath);
        Task<List<SmartaAttributeItem>> GetSmartAttributesAsync(string devicePath);
        Task<SmartaSelfTestStatus> GetSelfTestStatusAsync(string devicePath);
        Task<List<SmartaSelfTestEntry>> GetSelfTestLogAsync(string devicePath);
        Task<List<SmartaMaintenanceAction>> GetSupportedMaintenanceActionsAsync(string devicePath);
        Task<bool> ExecuteMaintenanceActionAsync(string devicePath, SmartaMaintenanceAction action);
        Task<bool> StartSelfTestAsync(string devicePath, SmartaSelfTestType testType);
    }
}
