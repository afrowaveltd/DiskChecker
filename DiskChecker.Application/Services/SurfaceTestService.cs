using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using DiskChecker.Infrastructure.Hardware;

namespace DiskChecker.Application.Services;

/// <summary>
/// Coordinates surface test execution and persistence.
/// </summary>
public class SurfaceTestService : ISurfaceTestService
{
    private readonly SurfaceTestExecutorFactory _executorFactory;
    private readonly SurfaceTestPersistenceService _persistenceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SurfaceTestService"/> class.
    /// </summary>
    /// <param name="executorFactory">Factory for creating appropriate test executors.</param>
    /// <param name="persistenceService">Persistence service for saving results.</param>
    public SurfaceTestService(SurfaceTestExecutorFactory executorFactory, SurfaceTestPersistenceService persistenceService)
    {
        _executorFactory = executorFactory;
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
        
        // Use factory to get appropriate executor
        var executor = _executorFactory.Create(normalizedRequest);

        var result = await executor.ExecuteAsync(normalizedRequest, progress, cancellationToken);

        var testId = await _persistenceService.SaveAsync(result, normalizedRequest.Drive, cancellationToken);
        result.TestId = testId.ToString();

        return result;
    }
}
