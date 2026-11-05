using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Historical low-level entry point. Reuse the real read-only scanner instead
/// of claiming that an unimplemented hardware operation succeeded.
/// </summary>
public sealed class DiskSurfaceTestExecutor : ISurfaceTestExecutor
{
    public DiskSurfaceTestExecutor(ISmartaProvider smartaProvider)
    {
        ArgumentNullException.ThrowIfNull(smartaProvider);
    }

    public async Task<SurfaceTestResult> ExecuteAsync(
        SurfaceTestRequest request,
        IProgress<SurfaceTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Drive);
        if (request.Operation != SurfaceTestOperation.ReadOnly)
            throw new NotSupportedException("Write tests require a dedicated confirmed destructive workflow.");

        var drive = request.Drive;
        var result = await SurfaceReadScanner.ScanAsync(drive.Path, drive.TotalSize,
            request.MaxBytesToTest, progress, cancellationToken);
        result.DriveModel = drive.Name ?? drive.Model;
        result.DriveSerialNumber = drive.SerialNumber;
        result.DriveInterface = drive.BusType.ToString();
        result.Technology = request.Technology;
        return result;
    }
}
