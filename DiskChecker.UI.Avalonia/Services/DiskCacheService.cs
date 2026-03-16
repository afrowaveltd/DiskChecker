using System.Collections.Immutable;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.Services;

public class DiskCacheService : IDiskCacheService
{
    private readonly IDiskDetectionService _diskDetectionService;
    private readonly object _lock = new();
    private IReadOnlyList<CoreDriveInfo>? _cachedDrives;
    private DateTime _cacheTimestamp;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
    private bool _isRefreshing;

    public event EventHandler? CacheInvalidated;

    public DiskCacheService(IDiskDetectionService diskDetectionService)
    {
        _diskDetectionService = diskDetectionService;
    }

    public async Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (!forceRefresh && _cachedDrives != null && DateTime.UtcNow - _cacheTimestamp < _cacheDuration)
            {
                return _cachedDrives;
            }
        }

        if (_isRefreshing)
        {
            await Task.Delay(50);
            return _cachedDrives ?? Array.Empty<CoreDriveInfo>();
        }

        _isRefreshing = true;
        try
        {
            var drives = await _diskDetectionService.GetDrivesAsync();
            var immutableDrives = drives.ToImmutableList();

            lock (_lock)
            {
                _cachedDrives = immutableDrives;
                _cacheTimestamp = DateTime.UtcNow;
            }

            CacheInvalidated?.Invoke(this, EventArgs.Empty);
            return immutableDrives;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            _cachedDrives = null;
            _cacheTimestamp = default;
        }
        CacheInvalidated?.Invoke(this, EventArgs.Empty);
    }
}