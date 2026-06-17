using System.Management;
using DiskChecker.TUI.Models;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Detects physical disks using WMI (Windows-only).
/// </summary>
public sealed class DiskDetector
{
    /// <summary>
    /// Enumerates all physical disks visible to the system.
    /// </summary>
    public List<PhysicalDiskInfo> DetectDisks()
    {
        var disks = new List<PhysicalDiskInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\Microsoft\Windows\Storage",
                "SELECT * FROM MSFT_PhysicalDisk");

            using var collection = searcher.Get();

            foreach (ManagementObject disk in collection)
            {
                try
                {
                    var info = new PhysicalDiskInfo
                    {
                        Index = Convert.ToInt32(disk["DeviceId"] ?? disk["PhysicalLocation"] ?? -1),
                        DevicePath = $@"\\.\PhysicalDrive{Convert.ToInt32(disk["DeviceId"] ?? -1)}",
                        Model = disk["Model"]?.ToString()?.Trim() ?? "Unknown",
                        SerialNumber = disk["SerialNumber"]?.ToString()?.Trim() ?? "N/A",
                        FirmwareRevision = disk["FirmwareVersion"]?.ToString()?.Trim() ?? "N/A",
                        CapacityBytes = Convert.ToUInt64(disk["Size"] ?? 0UL),
                        InterfaceType = disk["BusType"]?.ToString() ?? "Unknown",
                        IsRemovable = disk["MediaType"]?.ToString() == "Removable Media",
                        IsUsb = disk["BusType"]?.ToString()?.Contains("USB", StringComparison.OrdinalIgnoreCase) ?? false
                    };

                    if (info.CapacityBytes > 0)
                        disks.Add(info);
                }
                catch
                {
                    // Skip disks that can't be read
                }
            }
        }
        catch (ManagementException mex) when (mex.ErrorCode == ManagementStatus.InvalidNamespace)
        {
            // Fallback: try Win32_DiskDrive
            return DetectDisksFallback();
        }
        catch
        {
            return DetectDisksFallback();
        }

        return disks.OrderBy(d => d.Index).ToList();
    }

    private List<PhysicalDiskInfo> DetectDisksFallback()
    {
        var disks = new List<PhysicalDiskInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_DiskDrive");

            using var collection = searcher.Get();

            int index = 0;
            foreach (ManagementObject disk in collection)
            {
                try
                {
                    var info = new PhysicalDiskInfo
                    {
                        Index = Convert.ToInt32(disk["Index"] ?? index),
                        DevicePath = $@"\\.\PhysicalDrive{Convert.ToInt32(disk["Index"] ?? index)}",
                        Model = disk["Model"]?.ToString()?.Trim() ?? disk["Caption"]?.ToString()?.Trim() ?? "Unknown",
                        SerialNumber = disk["SerialNumber"]?.ToString()?.Trim() ?? "N/A",
                        FirmwareRevision = disk["FirmwareRevision"]?.ToString()?.Trim() ?? "N/A",
                        CapacityBytes = Convert.ToUInt64(disk["Size"] ?? 0UL),
                        InterfaceType = disk["InterfaceType"]?.ToString() ?? "Unknown",
                        IsRemovable = disk["MediaType"]?.ToString()?.Contains("Removable", StringComparison.OrdinalIgnoreCase) ?? false,
                        IsUsb = disk["InterfaceType"]?.ToString()?.Contains("USB", StringComparison.OrdinalIgnoreCase) ?? false
                    };

                    if (info.CapacityBytes > 0)
                        disks.Add(info);
                }
                catch
                {
                    // Skip
                }
                index++;
            }
        }
        catch
        {
            // No WMI available – return empty
        }

        return disks.OrderBy(d => d.Index).ToList();
    }
}
