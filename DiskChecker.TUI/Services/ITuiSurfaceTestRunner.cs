namespace DiskChecker.TUI.Services;

/// <summary>Seam for the surface test runner so screens stay unit testable.</summary>
public interface ITuiSurfaceTestRunner
{
    Task<TuiSurfaceTestReport> RunAsync(
        string path,
        long sizeBytes,
        TuiSurfaceTestOptions options,
        IProgress<SurfaceTestProgress>? progress,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class TuiSurfaceTestRunnerAdapter : ITuiSurfaceTestRunner
{
    public Task<TuiSurfaceTestReport> RunAsync(
        string path,
        long sizeBytes,
        TuiSurfaceTestOptions options,
        IProgress<SurfaceTestProgress>? progress,
        CancellationToken cancellationToken) =>
        TuiSurfaceTestRunner.RunAsync(path, sizeBytes, options, progress, cancellationToken);
}
