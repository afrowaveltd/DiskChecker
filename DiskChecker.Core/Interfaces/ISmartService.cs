using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Service interface for SMART data operations.
/// </summary>
public interface ISmartService
{
    /// <summary>
    /// Gets SMART data for a specific disk.
    /// </summary>
    Task<SmartCheckResult?> GetSmartDataAsync(DiskInfo disk);

    /// <summary>
    /// Gets a list of available disks with SMART information.
    /// </summary>
    Task<List<DiskInfo>> GetDisksWithSmartAsync();
}
