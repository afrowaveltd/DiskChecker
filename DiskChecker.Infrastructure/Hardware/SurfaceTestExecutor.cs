using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>Real read-only surface scan executor. Unsupported writes fail closed.</summary>
public sealed class SurfaceTestExecutor : ISurfaceTestExecutor
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
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Drive);
        if (request.Operation != SurfaceTestOperation.ReadOnly)
            throw new NotSupportedException(
                "This executor supports read-only surface tests only. " +
                "Use the explicitly confirmed sanitization workflow for destructive operations.");

        var drive = request.Drive;
        _logger.LogInformation("Starting read-only surface scan: {DrivePath}", drive.Path);
        var result = await SurfaceReadScanner.ScanAsync(drive.Path, drive.TotalSize,
            request.MaxBytesToTest, progress, cancellationToken);
        result.DriveModel = drive.Name ?? drive.Model;
        result.DriveSerialNumber = drive.SerialNumber;
        result.DriveInterface = drive.BusType.ToString();
        result.Technology = request.Technology;
        _logger.LogInformation("Surface scan finished: {BytesRead} bytes; {Errors} errors",
            result.TotalBytesTested, result.ErrorCount);
        return result;
    }
}
