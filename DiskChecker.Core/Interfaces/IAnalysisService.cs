using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces
{
    /// <summary>
    /// Service for analyzing disk surface and health.
    /// </summary>
    public interface IAnalysisService
    {
        /// <summary>
        /// Analyze surface of the specified disk.
        /// </summary>
        Task<IEnumerable<SurfaceTestResult>> AnalyzeSurfaceAsync(
            string deviceId,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Cancel ongoing analysis.
        /// </summary>
        Task CancelAnalysisAsync();
    }
}
