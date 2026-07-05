using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Read service for future analysis workspace. Keeps ViewModels from knowing
/// how telemetry/anomaly/seek data is split across persistence tables.
/// </summary>
public interface ITestAnalysisDataService
{
    Task<TestAnalysisData?> GetAnalysisDataAsync(int testSessionId, CancellationToken cancellationToken = default);
    Task<List<TestAnalysisSummary>> GetDiskAnalysisSummariesAsync(int diskCardId, CancellationToken cancellationToken = default);
}
