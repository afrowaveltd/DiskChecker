using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Platform-sensitive class for getting volume information on Linux systems.
/// </summary>
public static class LinuxVolumeInfoHelper
{
    /// <summary>
    /// Volume info result containing all details about volumes on a physical disk
    /// </summary>
    public class VolumeDetails
    {
        public string MountPoint { get; set; } = string.Empty;
        public string DevicePath { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long AvailableSpace { get; set; }
        
        public string DisplayText => string.IsNullOrEmpty(Label) 
            ? MountPoint 
            : $"{MountPoint} ({Label})";
    }
    
    /// <summary>
    /// Gets all volume information for a physical disk on Linux
    /// </summary>
    public static async Task<List<VolumeDetails>> GetVolumeDetailsAsync(string devicePath, ILogger? logger = null)
    {
        var result = new List<VolumeDetails>();
        
        try
        {
            // Use lsblk to get partition info
            var partitions = await GetPartitionsWithLsblkAsync(devicePath, logger);
            result.AddRange(partitions);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get volume details for {Path}", devicePath);
        }
        
        return result;
    }
    
    private static async Task<List<VolumeDetails>> GetPartitionsWithLsblkAsync(string devicePath, ILogger? logger = null)
    {
        var result = new List<VolumeDetails>();
        
        try
        {
            // Extract device name (e.g., /dev/sda -> sda)
            var deviceName = Path.GetFileName(devicePath);
            
            var psi = new ProcessStartInfo
            {
                FileName = "lsblk",
                Arguments = $"-J -b -o NAME,MOUNTPOINT,FSTYPE,LABEL,SIZE,UUID {deviceName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return result;
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (string.IsNullOrWhiteSpace(output)) return result;
            
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            
            if (!root.TryGetProperty("blockdevices", out var devices))
                return result;
            
            foreach (var device in devices.EnumerateArray())
            {
                // Process children (partitions)
                if (device.TryGetProperty("children", out var children))
                {
                    foreach (var partition in children.EnumerateArray())
                    {
                        var mountPoint = partition.TryGetProperty("mountpoint", out var mpProp) 
                            ? mpProp.GetString() ?? "" 
                            : "";
                        var fileSystem = partition.TryGetProperty("fstype", out var fsProp) 
                            ? fsProp.GetString() ?? "" 
                            : "";
                        var label = partition.TryGetProperty("label", out var labelProp) 
                            ? labelProp.GetString() ?? "" 
                            : "";
                        var size = partition.TryGetProperty("size", out var sizeProp) 
                            && long.TryParse(sizeProp.GetString(), out var s)
                            ? s 
                            : partition.TryGetProperty("size", out sizeProp) && sizeProp.ValueKind == JsonValueKind.Number
                                ? sizeProp.GetInt64() 
                                : 0;
                        var name = partition.TryGetProperty("name", out var nameProp) 
                            ? nameProp.GetString() ?? "" 
                            : "";
                        
                        if (!string.IsNullOrEmpty(mountPoint))
                        {
                            var volumeDetails = new VolumeDetails
                            {
                                MountPoint = mountPoint,
                                DevicePath = $"/dev/{name}",
                                FileSystem = fileSystem,
                                Label = label,
                                TotalSize = size
                            };
                            
                            // Get available space from mount point
                            volumeDetails.AvailableSpace = GetAvailableSpace(mountPoint);
                            
                            result.Add(volumeDetails);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get partition info via lsblk");
        }
        
        return result;
    }
    
    private static long GetAvailableSpace(string mountPoint)
    {
        try
        {
            var driveInfo = new DriveInfo(mountPoint);
            return driveInfo.AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }
    
    /// <summary>
    /// Gets a formatted string with volume info for display
    /// </summary>
    public static async Task<string> GetVolumeInfoAsync(string devicePath, ILogger? logger = null)
    {
        var volumes = await GetVolumeDetailsAsync(devicePath, logger);
        if (volumes.Count == 0)
            return string.Empty;
            
        return string.Join(", ", volumes.Select(v => v.DisplayText));
    }
    
    /// <summary>
    /// Checks if this is the system/boot disk
    /// </summary>
    public static async Task<bool> IsSystemDiskAsync(string devicePath, ILogger? logger = null)
    {
        try
        {
            var volumes = await GetVolumeDetailsAsync(devicePath, logger);
            return volumes.Any(v => v.MountPoint == "/");
        }
        catch
        {
            // Fallback: check if device path contains sda or nvme0n1
            return devicePath.Contains("sda") || devicePath.Contains("nvme0n1");
        }
    }
}