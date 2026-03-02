using Xunit;
using NSubstitute;
using DiskChecker.UI.WPF.ViewModels;
using DiskChecker.Core.Models;
using DiskChecker.Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiskChecker.Tests.WPF.ViewModels;

/// <summary>
/// Unit testy pro SurfaceTestViewModel.
/// </summary>
public class SurfaceTestViewModelTests : IDisposable
{
    private readonly ISurfaceTestService _surfaceTestService;
    private readonly SurfaceTestViewModel _viewModel;

    public SurfaceTestViewModelTests()
    {
        // Arrange - vytvoření mock objektů
        _surfaceTestService = Substitute.For<ISurfaceTestService>();
        
        _viewModel = new SurfaceTestViewModel(_surfaceTestService);
    }

    /// <summary>
    /// Test inicializace ViewModelu.
    /// </summary>
    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Assert
        Assert.False(_viewModel.IsTestRunning);
        Assert.Equal(0, _viewModel.ProgressPercent);
        Assert.Equal(0, _viewModel.BytesProcessed);
        Assert.Equal(0, _viewModel.ErrorCount);
        Assert.NotNull(_viewModel.Blocks);
        Assert.NotNull(_viewModel.SpeedSamples);
    }

    /// <summary>
    /// Test spuštění surface testu.
    /// </summary>
    [Fact]
    public async Task StartTest_ShouldUpdateProperties_WhenTestStarts()
    {
        // Arrange
        var testDrive = new CoreDriveInfo 
        { 
            Name = "TestDrive", 
            Path = "/dev/sda",
            TotalSize = 1000000000
        };
        _viewModel.SelectedDrive = testDrive;

        var testResult = new SurfaceTestResult
        {
            TestId = Guid.NewGuid().ToString(),
            DriveModel = "TestDrive"
        };

        // Mock test service to return immediately
        _surfaceTestService.RunAsync(
            Arg.Any<SurfaceTestRequest>(), 
            Arg.Any<IProgress<SurfaceTestProgress>>(), 
            Arg.Any<CancellationToken>()
        ).Returns(Task.FromResult(testResult));

        // Act
        await _viewModel.StartTestCommand.ExecuteAsync(null);

        // Assert
        Assert.False(_viewModel.IsTestRunning); // Should be false after completion
        await _surfaceTestService.Received(1).RunAsync(
            Arg.Any<SurfaceTestRequest>(),
            Arg.Any<IProgress<SurfaceTestProgress>>(),
            Arg.Any<CancellationToken>()
        );
    }

    /// <summary>
    /// Test že nelze spustit test bez vybraného disku.
    /// </summary>
    [Fact]
    public async Task StartTestCommand_ShouldNotExecute_WhenNoDriveSelected()
    {
        // Arrange
        _viewModel.SelectedDrive = null;

        // Act
        await _viewModel.StartTestCommand.ExecuteAsync(null);

        // Assert
        Assert.Contains("❌", _viewModel.StatusMessage);
    }

    /// <summary>
    /// Test zastavení běžícího testu.
    /// </summary>
    [Fact]
    public async Task StopTest_ShouldCancelTest_WhenTestIsRunning()
    {
        // Arrange
        var testDrive = new CoreDriveInfo 
        { 
            Name = "TestDrive", 
            Path = "/dev/sda",
            TotalSize = 1000000000
        };
        _viewModel.SelectedDrive = testDrive;

        var tcs = new TaskCompletionSource<SurfaceTestResult>();
        _surfaceTestService.RunAsync(
            Arg.Any<SurfaceTestRequest>(), 
            Arg.Any<IProgress<SurfaceTestProgress>>(), 
            Arg.Any<CancellationToken>()
        ).Returns(async x =>
        {
            var ct = x.ArgAt<CancellationToken>(2);
            try
            {
                await tcs.Task;
                ct.ThrowIfCancellationRequested();
                return new SurfaceTestResult();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        });

        // Act
        var startTask = _viewModel.StartTestCommand.ExecuteAsync(null);
        await Task.Delay(100); // Give test time to start
        _viewModel.StopTestCommand.Execute(null);
        tcs.SetCanceled(); // Allow the test to complete

        try
        {
            await startTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        Assert.False(_viewModel.IsTestRunning);
    }

    /// <summary>
    /// Test progress reportingu.
    /// </summary>
    [Fact]
    public async Task StartTest_ShouldReportProgress_WhenTestRuns()
    {
        // Arrange
        var testDrive = new CoreDriveInfo 
        { 
            Name = "TestDrive", 
            Path = "/dev/sda",
            TotalSize = 1000000000
        };
        _viewModel.SelectedDrive = testDrive;

        IProgress<SurfaceTestProgress>? capturedProgress = null;
        _surfaceTestService.RunAsync(
            Arg.Any<SurfaceTestRequest>(), 
            Arg.Do<IProgress<SurfaceTestProgress>>(x => capturedProgress = x), 
            Arg.Any<CancellationToken>()
        ).Returns(Task.FromResult(new SurfaceTestResult()));

        // Act
        await _viewModel.StartTestCommand.ExecuteAsync(null);

        // Assert
        Assert.NotNull(capturedProgress);
    }

    /// <summary>
    /// Test cleanup při dispose.
    /// </summary>
    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        var viewModel = new SurfaceTestViewModel(_surfaceTestService);

        // Act
        viewModel.Dispose();

        // Assert - no exception should be thrown
        Assert.True(true);
    }

    public void Dispose()
    {
        _viewModel?.Dispose();
        GC.SuppressFinalize(this);
    }
}
