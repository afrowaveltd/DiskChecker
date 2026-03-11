using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Models;
using DiskChecker.Core.Interfaces;
using DiskChecker.Application.Services;
using Microsoft.Extensions.Logging;
using System.Threading;
using System;

namespace DiskChecker.UI.Avalonia.Services
{
    #pragma warning disable CA1848 // Use LoggerMessage delegates
    /// <summary>
    /// Minimal implementation of IAnalysisService used by the UI.
    /// This is intentionally lightweight: it can be extended later to
    /// orchestrate application services (SurfaceTestService, analyzers, persistence).
    /// </summary>
    public class AnalysisService : IAnalysisService
    {
        private readonly ISurfaceTestService _surfaceTestService;
        private readonly TestReportAnalysisService _reportAnalyzer;
        private readonly ILogger<AnalysisService> _logger;

        // Protect cancellation source for single active analysis
        private CancellationTokenSource? _activeCts;

        public AnalysisService(ISurfaceTestService surfaceTestService, TestReportAnalysisService reportAnalyzer, ILogger<AnalysisService> logger)
        {
            _surfaceTestService = surfaceTestService ?? throw new ArgumentNullException(nameof(surfaceTestService));
            _reportAnalyzer = reportAnalyzer ?? throw new ArgumentNullException(nameof(reportAnalyzer));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task CancelAnalysisAsync()
        {
            try
            {
                _logger.LogInformation("Cancel requested for analysis");
                _activeCts?.Cancel();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while cancelling analysis");
            }
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<SurfaceTestResult>> AnalyzeSurfaceAsync(string deviceId, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("deviceId must be provided", nameof(deviceId));

            if (_activeCts != null)
                throw new InvalidOperationException("An analysis is already running");

            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                var linkedToken = _activeCts.Token;

                // Map progress from SurfaceTestProgress to integer percent
                var executorProgress = new Progress<SurfaceTestProgress>(p =>
                {
                    try
                    {
                        var percent = (int)Math.Round(p.PercentComplete);
                        progress?.Report(Math.Clamp(percent, 0, 100));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Progress reporter threw an exception");
                    }
                });

                var request = new SurfaceTestRequest
                {
                    Drive = new CoreDriveInfo { Path = deviceId, Name = deviceId, IsPhysical = true },
                    Profile = SurfaceTestProfile.SsdQuick,
                    Operation = SurfaceTestOperation.ReadOnly
                };

                var result = await _surfaceTestService.RunAsync(request, executorProgress, linkedToken).ConfigureAwait(false);

                try
                {
                    // Run post-processing/analysis (synchronous in current implementation)
                    _reportAnalyzer.AnalyzeResult(result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Report analysis failed for test {TestId}", result.TestId);
                }

                return new[] { result };
            }
            finally
            {
                _activeCts?.Dispose();
                _activeCts = null;
            }
        }

    #pragma warning restore CA1848
    }
}
