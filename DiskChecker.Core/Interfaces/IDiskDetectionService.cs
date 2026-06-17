using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Service for detecting and listing disk drives.
/// </summary>
public interface IDiskDetectionService
{
    /// <summary>
    /// Gets all detected disk drives.
    /// </summary>
    Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a specific drive by path.
    /// </summary>
    Task<CoreDriveInfo?> GetDriveAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects the connection speed (Mbps) and human-readable description for a drive.
    /// </summary>
    Task<(int? SpeedMbps, string? Description)> DetectConnectionSpeedAsync(string devicePath, CoreBusType busType, CancellationToken cancellationToken = default);
}