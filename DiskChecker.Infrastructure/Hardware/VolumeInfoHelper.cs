using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Helper class to get volume information for physical disks including:
/// - Mount points and drive letters
/// - Volume labels
/// - File system type
/// - Whether it's the system disk
/// </summary>
[SupportedOSPlatform("windows")]
public static class VolumeInfoHelper
{
    /// <summary>
    /// Volume info result containing all details about volumes on a physical disk
    /// </summary>
    public class VolumeDetails
    {
        public string DriveLetter { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public bool IsSystemDisk { get; set; }
        public bool IsBootDisk { get; set; }
        
        public string DisplayText => string.IsNullOrEmpty(VolumeLabel) 
            ? DriveLetter 
            : $"{DriveLetter} {VolumeLabel}";
    }
    
    /// <summary>
    /// Gets all volume information for a physical disk
    /// </summary>
    public static List<VolumeDetails> GetVolumeDetails(string physicalDrivePath, ILogger? logger = null)
    {
        var result = new List<VolumeDetails>();
        
        try
        {
            // Extract the drive number (e.g., "1" from "PhysicalDrive1")
            var driveMatch = Regex.Match(physicalDrivePath, @"PhysicalDrive(\d+)");
            if (!driveMatch.Success)
                return result;

            var driveNumber = driveMatch.Groups[1].Value;
            
            // Get the system/boot drive letter
            var systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.Windows).Substring(0, 1);
            var bootDrive = Environment.GetFolderPath(Environment.SpecialFolder.System).Substring(0, 1);
            
            // Query WMI for partitions on this physical disk
            var partitionQuery = $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='\\\\.\\PhysicalDrive{driveNumber}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition";
            
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", partitionQuery);
            
            foreach (ManagementObject partition in searcher.Get())
            {
                // Get volumes on this partition
                using var volumeQuery = new ManagementObjectSearcher(
                    "root\\CIMV2", 
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");
                
                foreach (ManagementObject volume in volumeQuery.Get())
                {
                    var driveLetter = volume["DeviceID"]?.ToString() ?? "";
                    var volumeLabel = volume["VolumeName"]?.ToString() ?? "";
                    var fileSystem = volume["FileSystem"]?.ToString() ?? "";
                    
                    if (!string.IsNullOrEmpty(driveLetter))
                    {
                        var details = new VolumeDetails
                        {
                            DriveLetter = driveLetter,
                            VolumeLabel = volumeLabel,
                            FileSystem = fileSystem,
                            IsSystemDisk = driveLetter.Equals(systemDrive, StringComparison.OrdinalIgnoreCase),
                            IsBootDisk = driveLetter.Equals(bootDrive, StringComparison.OrdinalIgnoreCase)
                        };
                        result.Add(details);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to get volume details for {Path}", physicalDrivePath);
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets a formatted string with volume info for display (e.g., "C: System")
    /// </summary>
    public static string GetVolumeInfo(string physicalDrivePath, ILogger? logger = null)
    {
        var volumes = GetVolumeDetails(physicalDrivePath, logger);
        if (!volumes.Any())
            return string.Empty;
            
        return string.Join(", ", volumes.Select(v => v.DisplayText));
    }
    
    /// <summary>
    /// Gets the file system type (e.g., "NTFS", "ext4")
    /// </summary>
    public static string GetFileSystem(string physicalDrivePath, ILogger? logger = null)
    {
        var volumes = GetVolumeDetails(physicalDrivePath, logger);
        // Return first non-empty file system
        return volumes.FirstOrDefault(v => !string.IsNullOrEmpty(v.FileSystem))?.FileSystem ?? string.Empty;
    }
    
    /// <summary>
    /// Checks if this is the system/boot disk
    /// </summary>
    public static bool IsSystemDisk(string physicalDrivePath, ILogger? logger = null)
    {
        var volumes = GetVolumeDetails(physicalDrivePath, logger);
        return volumes.Any(v => v.IsSystemDisk || v.IsBootDisk);
    }
}
