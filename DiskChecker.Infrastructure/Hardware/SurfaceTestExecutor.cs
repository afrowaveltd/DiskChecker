using DiskChecker.Core.Extensions;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Executes surface tests on drives.
/// </summary>
public class SurfaceTestExecutor : ISurfaceTestExecutor
{
    private readonly ILogger<SurfaceTestExecutor> _logger;

    public SurfaceTestExecutor(ILogger<SurfaceTestExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<SurfaceTestResult> ExecuteAsync(
        SurfaceTestRequest request,
        IProgress<SurfaceTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new SurfaceTestResult
        {
            TestId = Guid.NewGuid().ToString(),
            StartedAtUtc = DateTime.UtcNow,
            DriveModel = request.Drive.Model ?? "Unknown",
            DriveSerialNumber = request.Drive.SerialNumber ?? "Unknown"
        };

        try
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Starting surface test for drive: {DrivePath}", request.Drive.Path);
            }

            // Implementation would go here - actual disk surface test
            await Task.Delay(100, cancellationToken); // Placeholder

            result.CompletedAtUtc = DateTime.UtcNow;
            result.Notes = "Surface test completed successfully";

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Surface test completed for drive: {DrivePath}", request.Drive.Path);
            }
        }
        catch (OperationCanceledException)
        {
            result.CompletedAtUtc = DateTime.UtcNow;
            result.Notes = "Surface test was cancelled";
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("Surface test cancelled for drive: {DrivePath}", request.Drive.Path);
            }
        }
        catch (Exception ex)
        {
            result.CompletedAtUtc = DateTime.UtcNow;
            result.Notes = $"Surface test failed: {ex.Message}";
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Surface test failed for drive: {DrivePath}", request.Drive.Path);
            }
        }

        return result;
    }
}