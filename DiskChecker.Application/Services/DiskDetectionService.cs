using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Hardware;

namespace DiskChecker.Application.Services;

/// <summary>
/// Service for detecting and listing disk drives.
/// </summary>
public class DiskDetectionService : IDiskDetectionService
{
    // Cache for system disk path
    private string? _systemDiskPath;
    private int? _systemDiskNumber;
    
    public async Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<CoreDriveInfo>();
        
        // Detect system disk path/number (C: drive physical disk)
        (_systemDiskPath, _systemDiskNumber) = await GetSystemDiskIdentityAsync();
        
        // Get physical drives on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var physicalDrives = await GetPhysicalDrivesWindowsAsync(cancellationToken);
            
            // Mark system disk
            foreach (var drive in physicalDrives)
            {
                drive.IsSystemDisk = IsSystemDisk(drive.Path, drive.Name);
            }
            
            drives.AddRange(physicalDrives);
        }
        
        // If no physical drives found, try fallback
        if (drives.Count == 0)
        {
            var wmiDrives = await GetPhysicalDrivesWmiAsync(cancellationToken);
            foreach (var drive in wmiDrives)
            {
                drive.IsSystemDisk = IsSystemDisk(drive.Path, drive.Name);
            }
            drives.AddRange(wmiDrives);
        }
        
        // For each physical drive, try to enumerate its volumes (partitions)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var phys in drives.Where(d => d.IsPhysical).ToList())
            {
                try
                {
                    var vols = VolumeInfoHelper.GetVolumeDetails(phys.Path);
                    foreach (var v in vols)
                    {
                        try
                        {
                            var driveLetter = v.DriveLetter;
                            long totalSize = 0;
                            long freeSpace = 0;
                            string fs = v.FileSystem ?? string.Empty;
                            string name = v.VolumeLabel ?? driveLetter;
                            
                            if (!string.IsNullOrEmpty(driveLetter))
                            {
                                try
                                {
                                    var di = new DriveInfo(driveLetter.TrimEnd('\\'));
                                    if (di.IsReady)
                                    {
                                        totalSize = di.TotalSize;
                                        freeSpace = di.AvailableFreeSpace;
                                        fs = di.DriveFormat;
                                        name = !string.IsNullOrEmpty(di.VolumeLabel) ? di.VolumeLabel : driveLetter;
                                    }
                                }
                                catch { }
                            }

                            phys.Volumes.Add(new CoreDriveInfo
                            {
                                Id = Guid.NewGuid(),
                                Path = driveLetter ?? string.Empty,
                                Name = name,
                                TotalSize = totalSize,
                                FreeSpace = freeSpace,
                                FileSystem = fs,
                                IsPhysical = false,
                                IsReady = true,
                                VolumeInfo = fs
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        
        // If still no drives, add a placeholder for system disk
        if (drives.Count == 0)
        {
            drives.Add(new CoreDriveInfo
            {
                Id = Guid.NewGuid(),
                Path = _systemDiskPath ?? @"\\.\PHYSICALDRIVE0",
                Name = "System Drive (C:)",
                TotalSize = GetSystemDriveSize(),
                IsPhysical = true,
                IsSystemDisk = true,
                IsReady = true
            });
        }

        // Order: system disk first, then by bus type, then by model/name
        drives = drives
            .OrderByDescending(d => d.IsSystemDisk)
            .ThenBy(d => GetBusTypeSortOrder(d.BusType))
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        return drives.AsReadOnly();
    }
    
    private bool IsSystemDisk(string devicePath, string? name)
    {
        if (!string.IsNullOrEmpty(devicePath))
        {
            if (!string.IsNullOrEmpty(_systemDiskPath) && 
                devicePath.Equals(_systemDiskPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (_systemDiskNumber.HasValue)
            {
                var match = System.Text.RegularExpressions.Regex.Match(devicePath, @"PHYSICALDRIVE(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var n) && n == _systemDiskNumber.Value)
                {
                    return true;
                }
            }
            
            if (string.IsNullOrEmpty(_systemDiskPath) && 
                (devicePath.Contains("PhysicalDrive0", StringComparison.OrdinalIgnoreCase) ||
                 devicePath.Contains("PHYSICALDRIVE0", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        
        if (!string.IsNullOrEmpty(name) && name.Contains("C:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        return false;
    }
    
    private async Task<(string? Path, int? Number)> GetSystemDiskIdentityAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"$p = Get-Partition -DriveLetter C -ErrorAction SilentlyContinue; if ($p) { $d = $p | Get-Disk -ErrorAction SilentlyContinue; if ($d) { $d.Number } }\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return (null, null);
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                var lines = output.Trim().Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (int.TryParse(trimmed, out var number))
                    {
                        return ($@"\\.\PHYSICALDRIVE{number}", number);
                    }
                }
            }
        }
        catch { }
        
        return (null, null);
    }

    private static int GetBusTypeSortOrder(CoreBusType busType)
    {
        return busType switch
        {
            CoreBusType.Nvme => 0,
            CoreBusType.Sata => 1,
            CoreBusType.Sas => 2,
            CoreBusType.Usb => 3,
            CoreBusType.Ide => 4,
            _ => 5
        };
    }

    private async Task<string?> GetSystemDiskPathAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-Partition | Where-Object { $_.DriveLetter -eq 'C' } | Get-Disk | Select-Object -ExpandProperty DeviceId\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                var lines = output.Trim().Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"PHYSICALDRIVE(\d+)", 
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            return $@"\\.\PHYSICALDRIVE{match.Groups[1].Value}";
                        }
                    }
                }
            }
        }
        catch { }
        
        return null;
    }
    
    private static long GetSystemDriveSize()
    {
        try
        {
            var systemDrive = new DriveInfo("C");
            return systemDrive.TotalSize;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<List<CoreDriveInfo>> GetPhysicalDrivesWindowsAsync(CancellationToken cancellationToken)
    {
        var drives = new List<CoreDriveInfo>();
        
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_DiskDrive | Select-Object DeviceID, Model, Size, MediaType, FirmwareRevision, SerialNumber, InterfaceType | ConvertTo-Json -Compress\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return drives;
            
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            
            if (string.IsNullOrWhiteSpace(output)) return drives;
            
            using var doc = JsonDocument.Parse(output.Trim());
            var root = doc.RootElement;
            
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var drive = ParsePhysicalDrive(item);
                    if (drive != null) drives.Add(drive);
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                var drive = ParsePhysicalDrive(root);
                if (drive != null) drives.Add(drive);
            }
        }
        catch
        {
            try
            {
                drives = await GetPhysicalDrivesWmiAsync(cancellationToken);
            }
            catch
            {
                drives.Add(new CoreDriveInfo
                {
                    Id = Guid.NewGuid(),
                    Path = @"\\.\PHYSICALDRIVE0",
                    Name = "Physical Drive 0",
                    TotalSize = 0,
                    IsPhysical = true,
                    IsReady = true
                });
            }
        }
        
        return drives;
    }

    private async Task<List<CoreDriveInfo>> GetPhysicalDrivesWmiAsync(CancellationToken cancellationToken)
    {
        var drives = new List<CoreDriveInfo>();
        
        var psi = new ProcessStartInfo
        {
            FileName = "wmic",
            Arguments = "diskdrive get deviceid,model,size,interfacetype /format:list",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process == null) return drives;
        
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        
        var lines = output.Split('\n');
        string? currentDeviceId = null;
        string? currentModel = null;
        long currentSize = 0;
        string? currentInterface = null;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) 
            {
                if (!string.IsNullOrEmpty(currentDeviceId))
                {
                    drives.Add(new CoreDriveInfo
                    {
                        Id = Guid.NewGuid(),
                        Path = currentDeviceId,
                        Name = currentModel ?? "Unknown Drive",
                        TotalSize = currentSize,
                        IsPhysical = true,
                        IsReady = true,
                        Interface = currentInterface ?? "Unknown"
                    });
                    currentDeviceId = null;
                    currentModel = null;
                    currentSize = 0;
                    currentInterface = null;
                }
                continue;
            }
            
            if (trimmed.StartsWith("DeviceID=", StringComparison.OrdinalIgnoreCase))
                currentDeviceId = trimmed.AsSpan(9).ToString();
            else if (trimmed.StartsWith("Model=", StringComparison.OrdinalIgnoreCase))
                currentModel = trimmed.AsSpan(6).ToString();
            else if (trimmed.StartsWith("Size=", StringComparison.OrdinalIgnoreCase) && long.TryParse(trimmed.AsSpan(5), out var size))
                currentSize = size;
            else if (trimmed.StartsWith("InterfaceType=", StringComparison.OrdinalIgnoreCase))
                currentInterface = trimmed.AsSpan(14).ToString();
        }
        
        if (!string.IsNullOrEmpty(currentDeviceId))
        {
            drives.Add(new CoreDriveInfo
            {
                Id = Guid.NewGuid(),
                Path = currentDeviceId,
                Name = currentModel ?? "Unknown Drive",
                TotalSize = currentSize,
                IsPhysical = true,
                IsReady = true,
                Interface = currentInterface ?? "Unknown"
            });
        }
        
        return drives;
    }

    private CoreDriveInfo? ParsePhysicalDrive(JsonElement item)
    {
        try
        {
            var deviceId = item.TryGetProperty("DeviceID", out var idProp) 
                ? idProp.GetString() 
                : null;
            
            if (string.IsNullOrEmpty(deviceId)) return null;
            
            var model = item.TryGetProperty("Model", out var modelProp) 
                ? modelProp.GetString()?.Trim() 
                : "Unknown Drive";
            
            var size = item.TryGetProperty("Size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number
                ? sizeProp.GetInt64() 
                : 0;
            
            var interfaceType = item.TryGetProperty("InterfaceType", out var ifaceProp) 
                ? ifaceProp.GetString() 
                : "Unknown";
            
            var serialNumber = item.TryGetProperty("SerialNumber", out var serialProp) 
                ? serialProp.GetString()?.Trim() 
                : null;
            
            var mediaType = item.TryGetProperty("MediaType", out var mediaProp) 
                ? mediaProp.GetString() 
                : null;

            var isSolidState = IsSolidStateDrive(interfaceType, mediaType, model);
            var isRotational = isSolidState ? false : true;
            
            var busType = DetermineBusType(interfaceType);
            
            return new CoreDriveInfo
            {
                Id = Guid.NewGuid(),
                Path = deviceId,
                Name = model ?? "Unknown Drive",
                TotalSize = size,
                IsPhysical = true,
                IsReady = true,
                SerialNumber = serialNumber,
                MediaType = mediaType,
                Interface = interfaceType ?? "Unknown",
                BusType = busType,
                IsSolidState = isSolidState,
                IsRotational = isRotational
            };
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Determines whether a drive should be treated as solid-state based on available platform hints.
    /// </summary>
    private static bool IsSolidStateDrive(string? interfaceType, string? mediaType, string? model)
    {
        if (!string.IsNullOrWhiteSpace(interfaceType) && interfaceType.Contains("nvme", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            if (mediaType.Contains("ssd", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("solid", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("flash", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (mediaType.Contains("hdd", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Contains("hard", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            if (model.Contains("nvme", StringComparison.OrdinalIgnoreCase) ||
                model.Contains("ssd", StringComparison.OrdinalIgnoreCase) ||
                model.Contains("flash", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static CoreBusType DetermineBusType(string? interfaceType)
    {
        if (string.IsNullOrEmpty(interfaceType))
            return CoreBusType.Unknown;
        
        return interfaceType.ToLowerInvariant() switch
        {
            "nvme" => CoreBusType.Nvme,
            "sata" => CoreBusType.Sata,
            "usb" => CoreBusType.Usb,
            "sas" => CoreBusType.Sas,
            "ide" => CoreBusType.Ide,
            "scsi" => CoreBusType.Sata,
            _ => CoreBusType.Unknown
        };
    }

    public async Task<CoreDriveInfo?> GetDriveAsync(string path, CancellationToken cancellationToken = default)
    {
        var drives = await GetDrivesAsync(cancellationToken);
        return drives.FirstOrDefault(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }
}