using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

public interface ISmartaProvider
{
    Task<SmartaData?> GetSmartaDataAsync(string drivePath, CancellationToken cancellationToken = default);
    Task<bool> IsDriveValidAsync(string drivePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CoreDriveInfo>> ListDrivesAsync(CancellationToken cancellationToken = default);
}
