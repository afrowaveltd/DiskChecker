using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.Services.Interfaces;

/// <summary>
/// Service for managing disk selection and information.
/// </summary>
public interface IDiskSelectionService
{
    /// <summary>
    /// Get list of available disks.
    /// </summary>
    Task<IEnumerable<CoreDriveInfo>> GetAvailableDisksAsync();
    
    /// <summary>
    /// Get detailed information about a specific disk.
    /// </summary>
    Task<CoreDriveInfo?> GetDiskInfoAsync(string deviceId);
}
