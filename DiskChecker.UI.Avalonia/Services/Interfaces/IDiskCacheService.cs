using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.Services.Interfaces;

public interface IDiskCacheService
{
    Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(bool forceRefresh = false);
    void ClearCache();
    event EventHandler? CacheInvalidated;
}