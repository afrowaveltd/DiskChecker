using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Provides advanced SMART operations when underlying platform tooling supports it.
/// </summary>
public interface IAdvancedSmartaProvider
{
    /// <summary>
    /// Reads detailed SMART attributes for a drive.
    /// </summary>
    Task<IReadOnlyList<SmartaAttributeItem>> GetSmartAttributesAsync(string drivePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a SMART self-test on a drive.
    /// </summary>
    Task<bool> StartSelfTestAsync(string drivePath, SmartaSelfTestType selfTestType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current SMART self-test execution status.
    /// </summary>
    Task<SmartaSelfTestStatus?> GetSelfTestStatusAsync(string drivePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets SMART self-test log entries.
    /// </summary>
    Task<IReadOnlyList<SmartaSelfTestEntry>> GetSelfTestLogAsync(string drivePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets supported SMART maintenance actions for the specified drive.
    /// </summary>
    Task<IReadOnlyList<SmartaMaintenanceAction>> GetSupportedMaintenanceActionsAsync(string drivePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes selected SMART maintenance action.
    /// </summary>
    Task<bool> ExecuteMaintenanceActionAsync(string drivePath, SmartaMaintenanceAction action, CancellationToken cancellationToken = default);
}
