using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

public interface ISmartaProvider
{
    Task<SmartaData?> GetSmartaDataAsync(string drivePath, CancellationToken cancellationToken = default);
    Task<bool> IsDriveValidAsync(string drivePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoreDriveInfo>> ListDrivesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets ONLY the temperature from the drive (fast, works even during disk operations).
    /// Returns null if temperature cannot be determined.
    /// </summary>
    Task<int?> GetTemperatureOnlyAsync(string drivePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets instructions on how to install missing system dependencies.
    /// Returns null if all dependencies are satisfied.
    /// </summary>
    Task<string?> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to automatically install missing system dependencies.
    /// Returns true if installation was successful.
    /// </summary>
    Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default);
}
