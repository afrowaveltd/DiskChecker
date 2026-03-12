using System.Collections.Generic;
using System.Threading.Tasks;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Service for comparing disks and generating recommendations.
/// </summary>
public interface IDiskComparisonService
{
    /// <summary>
    /// Compares multiple disks and returns ranked results.
    /// </summary>
    /// <param name="diskCardIds">List of disk card IDs to compare</param>
    /// <returns>Ranked comparison results</returns>
    Task<List<DiskComparisonResult>> CompareAsync(List<int> diskCardIds);

    /// <summary>
    /// Gets the best disk from a list of disk card IDs.
    /// </summary>
    /// <param name="diskCardIds">List of disk card IDs</param>
    /// <returns>The best disk card or null if none found</returns>
    Task<DiskCard?> GetBestDiskAsync(List<int> diskCardIds);

    /// <summary>
    /// Gets recommended disks for a specific use case.
    /// </summary>
    /// <param name="useCase">Use case filter ("server", "workstation", "archive", "testing")</param>
    /// <param name="count">Maximum number of disks to return</param>
    /// <returns>List of recommended disks</returns>
    Task<List<DiskCard>> GetRecommendedDisksAsync(string useCase, int count = 5);

    /// <summary>
    /// Compares performance metrics between two disks.
    /// </summary>
    /// <param name="diskCardId1">First disk card ID</param>
    /// <param name="diskCardId2">Second disk card ID</param>
    /// <returns>Detailed performance comparison</returns>
    Task<PerformanceComparison> ComparePerformanceAsync(int diskCardId1, int diskCardId2);
}