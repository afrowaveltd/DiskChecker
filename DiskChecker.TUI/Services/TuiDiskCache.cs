using System.Collections.Immutable;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Short lived disk detection cache so navigating between screens does not re-probe
/// every device. Mirrors the desktop <c>DiskCacheService</c> behaviour.
/// </summary>
public interface ITuiDiskCache
{
    Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(bool forceRefresh = false);

    void ClearCache();
}

/// <inheritdoc />
public sealed class TuiDiskCache : ITuiDiskCache
{
    private readonly IDiskDetectionService _diskDetectionService;
    private readonly object _gate = new();
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(30);
    private IReadOnlyList<CoreDriveInfo>? _cachedDrives;
    private DateTime _cacheTimestamp;
    private bool _refreshing;

    public TuiDiskCache(IDiskDetectionService diskDetectionService) =>
        _diskDetectionService = diskDetectionService ?? throw new ArgumentNullException(nameof(diskDetectionService));

    public async Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(bool forceRefresh = false)
    {
        lock (_gate)
        {
            if (!forceRefresh && _cachedDrives is not null && DateTime.UtcNow - _cacheTimestamp < _cacheDuration)
            {
                return _cachedDrives;
            }
        }

        if (_refreshing)
        {
            await Task.Delay(50).ConfigureAwait(false);
            return _cachedDrives ?? Array.Empty<CoreDriveInfo>();
        }

        _refreshing = true;
        try
        {
            var drives = await _diskDetectionService.GetDrivesAsync().ConfigureAwait(false);
            var immutable = drives.ToImmutableList();
            lock (_gate)
            {
                _cachedDrives = immutable;
                _cacheTimestamp = DateTime.UtcNow;
            }

            return immutable;
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void ClearCache()
    {
        lock (_gate)
        {
            _cachedDrives = null;
            _cacheTimestamp = default;
        }
    }
}
