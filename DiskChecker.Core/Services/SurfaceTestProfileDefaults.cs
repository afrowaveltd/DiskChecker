using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Provides default surface test settings based on drive technology.
/// </summary>
public static class SurfaceTestProfileDefaults
{
    /// <summary>
    /// Applies default values to the provided request.
    /// </summary>
    /// <param name="request">Surface test request to fill.</param>
    /// <returns>A new request with defaults applied.</returns>
    public static SurfaceTestRequest ApplyDefaults(SurfaceTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var defaults = GetDefaults(request.Technology, request.Profile);

        return new SurfaceTestRequest
        {
            Drive = request.Drive,
            Technology = request.Technology,
            Profile = request.Profile,
            Operation = request.Operation == default ? defaults.Operation : request.Operation,
            BlockSizeBytes = request.BlockSizeBytes <= 0 ? defaults.BlockSizeBytes : request.BlockSizeBytes,
            SampleIntervalBlocks = request.SampleIntervalBlocks <= 0 ? defaults.SampleIntervalBlocks : request.SampleIntervalBlocks,
            MaxBytesToTest = request.MaxBytesToTest ?? defaults.MaxBytesToTest,
            SecureErase = request.SecureErase || defaults.SecureErase,
            AllowDeviceWrite = request.AllowDeviceWrite || defaults.AllowDeviceWrite,
            RequestedBy = request.RequestedBy
        };
    }

    /// <summary>
    /// Returns profile defaults for a given drive technology.
    /// </summary>
    /// <param name="technology">Drive technology.</param>
    /// <param name="profile">Requested profile.</param>
    /// <returns>Default request values for the profile.</returns>
    public static SurfaceTestRequest GetDefaults(DriveTechnology technology, SurfaceTestProfile profile)
    {
        return (technology, profile) switch
        {
            (DriveTechnology.Ssd, SurfaceTestProfile.SsdQuick) => new SurfaceTestRequest
            {
                Profile = SurfaceTestProfile.SsdQuick,
                Operation = SurfaceTestOperation.ReadOnly,
                BlockSizeBytes = 4 * 1024 * 1024,
                SampleIntervalBlocks = 64,
                MaxBytesToTest = 32L * 1024 * 1024 * 1024,
                SecureErase = false,
                AllowDeviceWrite = false
            },
            (DriveTechnology.Nvme, SurfaceTestProfile.SsdQuick) => new SurfaceTestRequest
            {
                Profile = SurfaceTestProfile.SsdQuick,
                Operation = SurfaceTestOperation.ReadOnly,
                BlockSizeBytes = 4 * 1024 * 1024,
                SampleIntervalBlocks = 64,
                MaxBytesToTest = 32L * 1024 * 1024 * 1024,
                SecureErase = false,
                AllowDeviceWrite = false
            },
            _ => new SurfaceTestRequest
            {
                Profile = SurfaceTestProfile.HddFull,
                Operation = SurfaceTestOperation.WriteZeroFill,
                BlockSizeBytes = 1024 * 1024,
                SampleIntervalBlocks = 128,
                MaxBytesToTest = null,
                SecureErase = false,
                AllowDeviceWrite = false
            }
        };
    }
}
