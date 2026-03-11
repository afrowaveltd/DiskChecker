using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DiskChecker.Tests
{
    public class AnalysisServiceTests
    {
        [Fact]
        public async Task AnalyzeSurfaceAsync_ForwardsProgressAndReturnsResult()
        {
            var surface = Substitute.For<ISurfaceTestService>();
            var analyzer = new TestReportAnalysisService();
            var logger = Substitute.For<ILogger<AnalysisService>>();

            var sampleResult = new SurfaceTestResult { TestId = "t1", CompletedAtUtc = DateTime.UtcNow };

            surface.RunAsync(Arg.Any<SurfaceTestRequest>(), Arg.Any<IProgress<SurfaceTestProgress>>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var progress = ci[1] as IProgress<SurfaceTestProgress>;
                    progress?.Report(new SurfaceTestProgress { TestId = Guid.NewGuid(), PercentComplete = 42 });
                    return Task.FromResult(sampleResult);
                });

            var sut = new AnalysisService(surface, analyzer, logger);

            int last = -1;
            var progress = new Progress<int>(p => last = p);

            var results = await sut.AnalyzeSurfaceAsync("PHYSICALDRIVE0", progress);

            Assert.Single(results);
            Assert.Equal(42, last);
        }

        [Fact]
        public async Task CancelAnalysisAsync_RequestsCancellation()
        {
            var surface = Substitute.For<ISurfaceTestService>();
            var analyzer = new TestReportAnalysisService();
            var logger = Substitute.For<ILogger<AnalysisService>>();

            var tcs = new TaskCompletionSource<SurfaceTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            surface.RunAsync(Arg.Any<SurfaceTestRequest>(), Arg.Any<IProgress<SurfaceTestProgress>>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var token = (CancellationToken)ci[2];
                    return Task.Run(async () =>
                    {
                        await Task.Delay(Timeout.Infinite, token);
                        return new SurfaceTestResult { TestId = "should-not-complete" };
                    }, token);
                });

            var sut = new AnalysisService(surface, analyzer, logger);

            var analyzeTask = sut.AnalyzeSurfaceAsync("PHYSICALDRIVE0", null);

            // Give it a moment to start
            await Task.Delay(50);

            await sut.CancelAnalysisAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analyzeTask);
        }
    }
}
