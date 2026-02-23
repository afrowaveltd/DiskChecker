using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Executes the low-level surface test operations for a drive.
/// </summary>
public interface ISurfaceTestExecutor
{
    /// <summary>
    /// Executes the requested surface test and returns the raw result.
    /// </summary>
    /// <param name="request">Surface test request to execute.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The surface test result.</returns>
    Task<SurfaceTestResult> ExecuteAsync(
        SurfaceTestRequest request,
        IProgress<SurfaceTestProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
