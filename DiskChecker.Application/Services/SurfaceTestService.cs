using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;

namespace DiskChecker.Application.Services;

/// <summary>
/// Coordinates surface test execution and persistence.
/// </summary>
public class SurfaceTestService : ISurfaceTestService
{
    private readonly ISurfaceTestExecutor _executor;
    private readonly SurfaceTestPersistenceService _persistenceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SurfaceTestService"/> class.
    /// </summary>
    /// <param name="executor">Surface test executor implementation.</param>
    /// <param name="persistenceService">Persistence service for saving results.</param>
    public SurfaceTestService(ISurfaceTestExecutor executor, SurfaceTestPersistenceService persistenceService)
    {
        _executor = executor;
        _persistenceService = persistenceService;
    }

    /// <inheritdoc />
    public async Task<SurfaceTestResult> RunAsync(
        SurfaceTestRequest request,
        IProgress<SurfaceTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Drive);

        var normalizedRequest = SurfaceTestProfileDefaults.ApplyDefaults(request);

        var result = await _executor.ExecuteAsync(normalizedRequest, progress, cancellationToken);

        var testId = await _persistenceService.SaveAsync(result, cancellationToken);
        result.TestId = testId;

        return result;
    }
}
