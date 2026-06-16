using System;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Executes seek tests on disk drives to measure seek latency and mechanical health.
/// </summary>
public interface ISeekTestExecutor
{
    /// <summary>
    /// Executes a seek test according to the provided request configuration.
    /// </summary>
    /// <param name="request">Test configuration including drive, type, and seek count.</param>
    /// <param name="progressCallback">Optional callback for progress updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Comprehensive test result with latency metrics.</returns>
    Task<SeekTestResult> ExecuteAsync(
        SeekTestRequest request,
        Action<SeekTestProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a SMART-informed recommendation for seek test parameters.
    /// Uses PowerOnHours, reallocated sectors, and drive type to determine
    /// appropriate seek count and test type.
    /// </summary>
    /// <param name="smartaData">SMART data for the drive (can be null if unavailable).</param>
    /// <param name="driveTotalBytes">Total drive capacity in bytes.</param>
    /// <param name="isSolidState">Whether the drive is an SSD.</param>
    /// <returns>Recommendation with suggested parameters and rationale.</returns>
    SeekTestRecommendation GetRecommendation(
        SmartaData? smartaData,
        long driveTotalBytes,
        bool isSolidState);

    /// <summary>
    /// Checks whether the platform supports direct device access for seek testing.
    /// </summary>
    Task<bool> IsPlatformSupportedAsync(CancellationToken cancellationToken = default);
}
