using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services;

/// <summary>
/// High-level service for disk checking operations.
/// </summary>
public class DiskCheckerService
{
    private readonly ISmartaProvider _smartaProvider;
    private readonly IQualityCalculator _qualityCalculator;

    public DiskCheckerService(ISmartaProvider smartaProvider, IQualityCalculator qualityCalculator)
    {
        _smartaProvider = smartaProvider;
        _qualityCalculator = qualityCalculator;
    }

    public async Task<SmartaData?> GetDiskInfoAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        return await _smartaProvider.GetSmartaDataAsync(drivePath, cancellationToken);
    }

    public async Task<QualityRating?> CalculateQualityAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        var smartaData = await _smartaProvider.GetSmartaDataAsync(drivePath, cancellationToken);
        if (smartaData == null)
        {
            return null;
        }

        return _qualityCalculator.CalculateQuality(smartaData);
    }

    public async Task<bool> IsDriveValidAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        return await _smartaProvider.IsDriveValidAsync(drivePath, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListDrivesAsync(CancellationToken cancellationToken = default)
    {
        return await _smartaProvider.ListDrivesAsync(cancellationToken);
    }
}