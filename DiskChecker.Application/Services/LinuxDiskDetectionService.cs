using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services;

/// <summary>
/// Service for detecting and listing disk drives on Linux systems.
/// Uses lsblk and /sys filesystem for disk information.
/// </summary>
public class LinuxDiskDetectionService : IDiskDetectionService
{
    public async Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<CoreDriveInfo>();
        
        try
        {
            // Use lsblk to get disk information
            var lsblkOutput = await ExecuteCommandAsync("lsblk", "-J -b -o NAME,SIZE,TYPE,MOUNTPOINT,MODEL,SERIAL,FSTYPE,LABEL", cancellationToken);
            
            if (!string.IsNullOrEmpty(lsblkOutput))
            {
                drives = ParseLsblkOutput(lsblkOutput);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LinuxDiskDetectionService] lsblk failed: {ex.Message}");
        }
        
        // Fallback: read from /sys/block and /proc/partitions
        if (drives.Count == 0)
        {
            drives = await GetDrivesFromSysfsAsync(cancellationToken);
        }
        
        // Mark system disk (root filesystem)
        await MarkSystemDiskAsync(drives, cancellationToken);
        
        return drives.AsReadOnly();
    }

    private List<CoreDriveInfo> ParseLsblkOutput(string json)
    {
        var drives = new List<CoreDriveInfo>();
        
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (!root.TryGetProperty("blockdevices", out var devices))
                return drives;
            
            foreach (var device in devices.EnumerateArray())
            {
                var type = device.TryGetProperty("type", out var typeProp) 
                    ? typeProp.GetString() ?? "" 
                    : "";
                
                // Only process whole disks, not partitions
                if (!string.Equals(type, "disk", StringComparison.Ordinal))
                    continue;
                
                var name = device.TryGetProperty("name", out var nameProp) 
                    ? nameProp.GetString() ?? "" 
                    : "";
                
                if (string.IsNullOrEmpty(name))
                    continue;
                
                var size = device.TryGetProperty("size", out var sizeProp) 
                    && long.TryParse(sizeProp.GetString(), out var sizeVal) 
                    ? sizeVal 
                    : device.TryGetProperty("size", out sizeProp) && sizeProp.ValueKind == JsonValueKind.Number
                        ? sizeProp.GetInt64() 
                        : 0;
                
                var model = device.TryGetProperty("model", out var modelProp) 
                    ? modelProp.GetString() 
                    : null;
                
                var serial = device.TryGetProperty("serial", out var serialProp) 
                    ? serialProp.GetString() 
                    : null;
                
                // Build path for /dev device
                var path = $"/dev/{name}";
                
                // Get mount points from children (partitions)
                var mountPoints = new List<(string MountPoint, string FsType, string Label)>();
                if (device.TryGetProperty("children", out var children))
                {
                    foreach (var child in children.EnumerateArray())
                    {
                        var mountPoint = child.TryGetProperty("mountpoint", out var mpProp) 
                            ? mpProp.GetString() 
                            : null;
                        var fsType = child.TryGetProperty("fstype", out var fsProp) 
                            ? fsProp.GetString() 
                            : null;
                        var label = child.TryGetProperty("label", out var labelProp) 
                            ? labelProp.GetString() 
                            : null;
                        
                        if (!string.IsNullOrEmpty(mountPoint))
                        {
                            mountPoints.Add((mountPoint, fsType ?? "", label ?? ""));
                        }
                    }
                }
                
                var drive = new CoreDriveInfo
                {
                    Id = Guid.NewGuid(),
                    Path = path,
                    Name = $"{model ?? name} ({path})",
                    TotalSize = size,
                    IsPhysical = true,
                    IsReady = true,
                    SerialNumber = serial
                };
                
                // Add info about mounted partitions
                foreach (var (mountPoint, fsType, label) in mountPoints)
                {
                    drive.Volumes.Add(new CoreDriveInfo
                    {
                        Id = Guid.NewGuid(),
                        Path = mountPoint,
                        Name = label,
                        FileSystem = fsType,
                        IsPhysical = false,
                        IsReady = true
                    });
                }
                
                drives.Add(drive);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LinuxDiskDetectionService] ParseLsblkOutput failed: {ex.Message}");
        }
        
        return drives;
    }

    private async Task<List<CoreDriveInfo>> GetDrivesFromSysfsAsync(CancellationToken cancellationToken)
    {
        var drives = new List<CoreDriveInfo>();
        
        try
        {
            var blockDevicesPath = "/sys/block";
            if (!Directory.Exists(blockDevicesPath))
                return drives;
            
            foreach (var devicePath in Directory.GetDirectories(blockDevicesPath))
            {
                var deviceName = Path.GetFileName(devicePath);
                
                // Skip loop devices and RAM disks
                if (deviceName.StartsWith("loop", StringComparison.Ordinal) || 
                    deviceName.StartsWith("ram", StringComparison.Ordinal) || 
                    deviceName.StartsWith("zram", StringComparison.Ordinal))
                {
                    continue;
                }
                
                // Read size in sectors
                var sizePath = Path.Combine(devicePath, "size");
                long sizeBytes = 0;
                if (File.Exists(sizePath))
                {
                    var sizeText = await File.ReadAllTextAsync(sizePath, cancellationToken);
                    if (long.TryParse(sizeText.Trim(), out var sectors))
                    {
                        sizeBytes = sectors * 512; // Standard sector size
                    }
                }
                
                // Read device model
                var modelPath = Path.Combine(devicePath, "device", "model");
                string? model = null;
                if (File.Exists(modelPath))
                {
                    model = (await File.ReadAllTextAsync(modelPath, cancellationToken)).Trim();
                }
                
                // Read vendor if available
                var vendorPath = Path.Combine(devicePath, "device", "vendor");
                string? vendor = null;
                if (File.Exists(vendorPath))
                {
                    vendor = (await File.ReadAllTextAsync(vendorPath, cancellationToken)).Trim();
                }
                
                // Read serial if available
                var serialPath = Path.Combine(devicePath, "device", "serial");
                string? serial = null;
                if (File.Exists(serialPath))
                {
                    serial = (await File.ReadAllTextAsync(serialPath, cancellationToken)).Trim();
                }
                
                var displayName = string.IsNullOrEmpty(model) 
                    ? $"/dev/{deviceName}" 
                    : $"{model} (/dev/{deviceName})";
                
                if (!string.IsNullOrEmpty(vendor) && !displayName.Contains(vendor))
                {
                    displayName = $"{vendor} {displayName}";
                }
                
                drives.Add(new CoreDriveInfo
                {
                    Id = Guid.NewGuid(),
                    Path = $"/dev/{deviceName}",
                    Name = displayName,
                    TotalSize = sizeBytes,
                    IsPhysical = true,
                    IsReady = true,
                    SerialNumber = serial
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LinuxDiskDetectionService] GetDrivesFromSysfsAsync failed: {ex.Message}");
        }
        
        return drives;
    }

    private async Task MarkSystemDiskAsync(List<CoreDriveInfo> drives, CancellationToken cancellationToken)
    {
        try
        {
            // Find which device has / mounted
            var findmntOutput = await ExecuteCommandAsync("findmnt", "-n -o SOURCE /", cancellationToken);
            
            if (!string.IsNullOrEmpty(findmntOutput))
            {
                // findmnt returns something like /dev/sda2 or /dev/nvme0n1p2
                var rootPartition = findmntOutput.Trim();
                var rootDisk = GetParentDiskFromPartition(rootPartition);
                
                foreach (var drive in drives)
                {
                    if (string.Equals(drive.Path, rootDisk, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(drive.Path, rootPartition, StringComparison.OrdinalIgnoreCase))
                    {
                        drive.IsSystemDisk = true;
                    }
                }
            }
        }
        catch
        {
            // Fallback: assume sda or nvme0n1 is system disk
            var systemDisk = drives.FirstOrDefault(d => 
                d.Path.Contains("sda", StringComparison.OrdinalIgnoreCase) || 
                d.Path.Contains("nvme0n1", StringComparison.OrdinalIgnoreCase));
            if (systemDisk != null)
            {
                systemDisk.IsSystemDisk = true;
            }
        }
    }

    private static string GetParentDiskFromPartition(string partitionPath)
    {
        // /dev/sda2 -> /dev/sda
        // /dev/nvme0n1p2 -> /dev/nvme0n1
        // /dev/sdb3 -> /dev/sdb
        
        var match = Regex.Match(partitionPath, @"^(/dev/(?:nvme\d+n\d+|sd[a-z]|vd[a-z]|hd[a-z]))", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        
        // Try to remove partition number suffix
        var parentPath = Regex.Replace(partitionPath, @"p?\d+$", "");
        return parentPath;
    }

    private static async Task<string?> ExecuteCommandAsync(string command, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return null;
            
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            
            return output;
        }
        catch
        {
            return null;
        }
    }

    public async Task<CoreDriveInfo?> GetDriveAsync(string path, CancellationToken cancellationToken = default)
    {
        var drives = await GetDrivesAsync(cancellationToken);
        return drives.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
    }
}