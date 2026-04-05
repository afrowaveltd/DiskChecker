using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services;

public class LinuxDiskDetectionService : IDiskDetectionService
{
    public async Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<CoreDriveInfo>();
        
        try
        {
            var lsblkOutput = await ExecuteCommandAsync("lsblk", "-J -b -o NAME,SIZE,TYPE,MOUNTPOINT,MODEL,SERIAL,FSTYPE,LABEL,PARTLABEL,ROTA,TRAN,WWN", cancellationToken);
            
            if (!string.IsNullOrEmpty(lsblkOutput))
            {
                drives = ParseLsblkOutput(lsblkOutput);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LinuxDiskDetectionService] lsblk failed: {ex.Message}");
        }
        
        if (drives.Count == 0)
        {
            drives = await GetDrivesFromSysfsAsync(cancellationToken);
        }
        
        foreach (var drive in drives)
        {
            try
            {
                await EnrichWithPartitionInfoAsync(drive, cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinuxDiskDetectionService] Failed to enrich partition info: {ex.Message}");
            }
        }
        
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
                    ? modelProp.GetString()?.Trim() 
                    : null;
                
                var serial = device.TryGetProperty("serial", out var serialProp) 
                    ? serialProp.GetString()?.Trim() 
                    : null;
                
                var transport = device.TryGetProperty("tran", out var tranProp) 
                    ? tranProp.GetString() 
                    : null;
                
                var rotational = device.TryGetProperty("rota", out var rotaProp) 
                    && rotaProp.ValueKind == JsonValueKind.String
                    && rotaProp.GetString() == "0"
                    ? false 
                    : true;
                
                var path = $"/dev/{name}";
                
                var displayNameParts = new List<string>();
                if (!string.IsNullOrEmpty(model))
                    displayNameParts.Add(model);
                else
                    displayNameParts.Add(name);
                
                if (!string.IsNullOrEmpty(transport))
                {
                    displayNameParts.Add($"({transport.ToUpperInvariant()})");
                }
                
                var displayName = string.Join(" ", displayNameParts);
                var busType = DetermineBusType(transport, rotational);
                var isSolidState = !rotational || string.Equals(transport, "nvme", StringComparison.OrdinalIgnoreCase);
                var isRotational = rotational;
                
                var mountPoints = new List<(string MountPoint, string FsType, string Label, string DevicePath, long Size)>();
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
                        var partLabel = child.TryGetProperty("partlabel", out var plProp)
                            ? plProp.GetString()
                            : null;
                        var partitionName = child.TryGetProperty("name", out var pnProp) 
                            ? pnProp.GetString() 
                            : null;
                        var partitionSize = child.TryGetProperty("size", out var psProp) 
                            && long.TryParse(psProp.GetString(), out var pSize) 
                            ? pSize 
                            : 0;
                        
                        if (!string.IsNullOrEmpty(partitionName))
                        {
                            var devicePath = $"/dev/{partitionName}";
                            mountPoints.Add((
                                mountPoint ?? string.Empty,
                                fsType ?? string.Empty,
                                !string.IsNullOrWhiteSpace(label) ? label! : partLabel ?? string.Empty,
                                devicePath,
                                partitionSize
                            ));
                        }
                    }
                }
                
                var drive = new CoreDriveInfo
                {
                    Id = Guid.NewGuid(),
                    Path = path,
                    Name = displayName,
                    TotalSize = size,
                    IsPhysical = true,
                    IsReady = true,
                    SerialNumber = serial,
                    Model = model,
                    Interface = transport ?? "Unknown",
                    BusType = busType,
                    IsSolidState = isSolidState,
                    IsRotational = isRotational
                };
                
                foreach (var (mountPoint, fsType, label, devicePath, pSize) in mountPoints)
                {
                    var displayPath = string.IsNullOrWhiteSpace(mountPoint) ? devicePath : mountPoint;
                    var volumeDisplayName = BuildLinuxVolumeName(label, fsType, devicePath);

                    drive.Volumes.Add(new CoreDriveInfo
                    {
                        Id = Guid.NewGuid(),
                        Path = displayPath,
                        Name = volumeDisplayName,
                        TotalSize = pSize,
                        FileSystem = string.IsNullOrWhiteSpace(fsType) ? "Unknown" : fsType,
                        IsPhysical = false,
                        IsReady = true,
                        VolumeInfo = devicePath
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
    
    private static CoreBusType DetermineBusType(string? transport, bool isRotational)
    {
        if (string.IsNullOrEmpty(transport))
        {
            return isRotational ? CoreBusType.Sata : CoreBusType.Unknown;
        }
        
        return transport.ToLowerInvariant() switch
        {
            "nvme" => CoreBusType.Nvme,
            "sata" => CoreBusType.Sata,
            "usb" => CoreBusType.Usb,
            "sas" => CoreBusType.Sas,
            "ide" => CoreBusType.Ide,
            _ => CoreBusType.Unknown
        };
    }

    private async Task EnrichWithPartitionInfoAsync(CoreDriveInfo drive, CancellationToken cancellationToken)
    {
        try
        {
            var deviceName = Path.GetFileName(drive.Path);
            var lsblkOutput = await ExecuteCommandAsync("lsblk", $"-J -b -o NAME,MOUNTPOINT,FSTYPE,LABEL,PARTLABEL,SIZE {deviceName}", cancellationToken);
            
            if (string.IsNullOrEmpty(lsblkOutput))
                return;
            
            using var doc = JsonDocument.Parse(lsblkOutput);
            var root = doc.RootElement;
            
            if (!root.TryGetProperty("blockdevices", out var devices))
                return;
            
            drive.Volumes.Clear();
            
            foreach (var device in devices.EnumerateArray())
            {
                if (device.TryGetProperty("children", out var children))
                {
                    foreach (var partition in children.EnumerateArray())
                    {
                        var mountPoint = partition.TryGetProperty("mountpoint", out var mpProp) 
                            ? mpProp.GetString()?.Trim() 
                            : null;
                        var fileSystem = partition.TryGetProperty("fstype", out var fsProp) 
                            ? fsProp.GetString() 
                            : null;
                        var label = partition.TryGetProperty("label", out var labelProp) 
                            ? labelProp.GetString() 
                            : null;
                        var partLabel = partition.TryGetProperty("partlabel", out var plProp)
                            ? plProp.GetString()
                            : null;
                        var partitionName = partition.TryGetProperty("name", out var nameProp) 
                            ? nameProp.GetString() 
                            : null;
                        var partitionSize = partition.TryGetProperty("size", out var sizeProp) 
                            && long.TryParse(sizeProp.GetString(), out var s)
                            ? s 
                            : 0;
                        
                        if (!string.IsNullOrEmpty(partitionName))
                        {
                            var devicePath = $"/dev/{partitionName}";
                            var displayPath = string.IsNullOrWhiteSpace(mountPoint) || mountPoint == "null"
                                ? devicePath
                                : mountPoint;
                            var displayName = BuildLinuxVolumeName(
                                !string.IsNullOrWhiteSpace(label) ? label : partLabel,
                                fileSystem,
                                devicePath);

                            drive.Volumes.Add(new CoreDriveInfo
                            {
                                Id = Guid.NewGuid(),
                                Path = displayPath,
                                Name = displayName,
                                TotalSize = partitionSize,
                                FileSystem = string.IsNullOrWhiteSpace(fileSystem) ? "Unknown" : fileSystem,
                                IsPhysical = false,
                                IsReady = true,
                                VolumeInfo = devicePath
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LinuxDiskDetectionService] EnrichWithPartitionInfoAsync failed: {ex.Message}");
        }
    }

    private static string BuildLinuxVolumeName(string? label, string? fileSystem, string devicePath)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label!;
        }

        if (!string.IsNullOrWhiteSpace(fileSystem))
        {
            return $"{devicePath} ({fileSystem})";
        }

        return devicePath;
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
                
                if (deviceName.StartsWith("loop", StringComparison.Ordinal) || 
                    deviceName.StartsWith("ram", StringComparison.Ordinal) || 
                    deviceName.StartsWith("zram", StringComparison.Ordinal))
                {
                    continue;
                }
                
                var sizePath = Path.Combine(devicePath, "size");
                long sizeBytes = 0;
                if (File.Exists(sizePath))
                {
                    var sizeText = await File.ReadAllTextAsync(sizePath, cancellationToken);
                    if (long.TryParse(sizeText.Trim(), out var sectors))
                    {
                        sizeBytes = sectors * 512;
                    }
                }
                
                var modelPath = Path.Combine(devicePath, "device", "model");
                string? model = null;
                if (File.Exists(modelPath))
                {
                    model = (await File.ReadAllTextAsync(modelPath, cancellationToken)).Trim();
                }
                
                var serialPath = Path.Combine(devicePath, "device", "serial");
                string? serial = null;
                if (File.Exists(serialPath))
                {
                    serial = (await File.ReadAllTextAsync(serialPath, cancellationToken)).Trim();
                }
                
                var displayName = string.IsNullOrEmpty(model) 
                    ? $"/dev/{deviceName}" 
                    : $"{model} (/dev/{deviceName})";
                
                drives.Add(new CoreDriveInfo
                {
                    Id = Guid.NewGuid(),
                    Path = $"/dev/{deviceName}",
                    Name = displayName,
                    TotalSize = sizeBytes,
                    IsPhysical = true,
                    IsReady = true,
                    SerialNumber = serial,
                    IsSolidState = null,
                    IsRotational = null
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
            var findmntOutput = await ExecuteCommandAsync("findmnt", "-n -o SOURCE /", cancellationToken);
            
            if (!string.IsNullOrEmpty(findmntOutput))
            {
                var rootPartition = findmntOutput.Trim();
                var rootDisk = GetParentDiskFromPartition(rootPartition);
                
                foreach (var drive in drives)
                {
                    if (string.Equals(drive.Path, rootDisk, StringComparison.OrdinalIgnoreCase))
                    {
                        drive.IsSystemDisk = true;
                    }
                    
                    if (!drive.IsSystemDisk && drive.Volumes.Any(v => v.Path == "/"))
                    {
                        drive.IsSystemDisk = true;
                    }
                }
            }
        }
        catch
        {
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
        var match = Regex.Match(partitionPath, @"^(/dev/(?:nvme\d+n\d+|sd[a-z]|vd[a-z]|hd[a-z]))", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        
        var parentPath = Regex.Replace(partitionPath, @"p?\d+$", "");
        return parentPath;
    }

    private static async Task<string?> ExecuteCommandAsync(string command, string arguments, CancellationToken cancellationToken = default)
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