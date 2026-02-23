using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Defines surface test execution for storage devices.
/// </summary>
public interface ISurfaceTestService
{
    /// <summary>
    /// Executes a surface test based on the provided request.
    /// </summary>
    /// <param name="request">Surface test request.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The surface test result.</returns>
    Task<SurfaceTestResult> RunAsync(
        SurfaceTestRequest request,
        IProgress<SurfaceTestProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
