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
                
                // Validate serial - lsblk may return unreliable serials from USB bridges
                // If serial is unreliable, try to get a better one from /dev/disk/by-id later
                var validatedSerial = serial;
                if (!string.IsNullOrWhiteSpace(validatedSerial) && 
                    !DiskChecker.Application.Services.DriveIdentityResolver.IsReliableSerialNumber(validatedSerial))
                {
                    validatedSerial = null; // Will be resolved later via sysfs or by-id
                }
                
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
                    SerialNumber = validatedSerial,
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
                
                var serial = await ResolveStableLinuxIdentifierAsync(deviceName, devicePath, cancellationToken);
                
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

    private static async Task<string?> ResolveStableLinuxIdentifierAsync(string deviceName, string devicePath, CancellationToken cancellationToken)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(devicePath, "device", "serial"),
                     Path.Combine(devicePath, "wwid"),
                     Path.Combine(devicePath, "device", "wwid")
                 })
        {
            var value = await ReadTrimmedFileAsync(candidate, cancellationToken);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        try
        {
            const string byIdPath = "/dev/disk/by-id";
            if (Directory.Exists(byIdPath))
            {
                var matches = Directory.EnumerateFileSystemEntries(byIdPath)
                    .Where(entry => !entry.Contains("-part", StringComparison.OrdinalIgnoreCase))
                    .Select(entry => new
                    {
                        Name = Path.GetFileName(entry),
                        Target = ResolveSymlinkTarget(entry)
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Target) &&
                                string.Equals(Path.GetFileName(x.Target), deviceName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => GetIdentifierPriority(x.Name))
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var best = matches.FirstOrDefault();
                if (best != null)
                {
                    return best.Name;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static async Task<string?> ReadTrimmedFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var value = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveSymlinkTarget(string entry)
    {
        try
        {
            var info = new FileInfo(entry);
            return info.ResolveLinkTarget(true)?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static int GetIdentifierPriority(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 99;
        if (name.StartsWith("wwn-", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith("nvme-eui.", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.StartsWith("nvme-", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.StartsWith("ata-", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.StartsWith("scsi-", StringComparison.OrdinalIgnoreCase)) return 4;
        if (name.StartsWith("usb-", StringComparison.OrdinalIgnoreCase)) return 5;
        return 9;
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
            
            // Read output and error concurrently to avoid deadlocks
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            
            // Wait for process to exit with a timeout to prevent hanging on unresponsive disks
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout reached - kill the process
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore errors killing the process
                }
                
                Debug.WriteLine($"[LinuxDiskDetectionService] Command timed out: {command} {arguments}");
                return null;
            }
            
            var output = await outputTask;
            return output;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // External cancellation - propagate
            return null;
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

    public async Task<(int? SpeedMbps, string? Description)> DetectConnectionSpeedAsync(string devicePath, CoreBusType busType, CancellationToken cancellationToken = default)
    {
        // For NVMe, read PCIe link speed from sysfs
        if (busType == CoreBusType.Nvme)
        {
            try
            {
                var deviceName = Path.GetFileName(devicePath);
                var nvmeController = deviceName.Replace("n1", "").Replace("p1", "");
                
                var linkSpeedPaths = new[]
                {
                    $"/sys/block/{deviceName}/device/device/link_speed",
                    $"/sys/class/nvme/{nvmeController}/device/link_speed",
                };

                foreach (var path in linkSpeedPaths)
                {
                    if (File.Exists(path))
                    {
                        var content = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
                        var speedMatch = Regex.Match(content, @"(\d+\.?\d*)\s*GT/s");
                        if (speedMatch.Success && double.TryParse(speedMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var gtPerSec))
                        {
                            var lanes = 4;
                            var encodingEfficiency = 0.985;
                            var rawMbps = gtPerSec * 1000 * lanes * encodingEfficiency;
                            var gen = gtPerSec >= 16 ? "Gen5" : gtPerSec >= 8 ? "Gen4" : gtPerSec >= 4 ? "Gen3" : "Gen2";
                            return ((int)rawMbps, $"NVMe PCIe {gen} x{lanes} ({gtPerSec} GT/s, až {(int)rawMbps} Mbps)");
                        }
                    }
                }
            }
            catch { }
            return (32000, "NVMe (až 32 000 Mbps)");
        }

        // For USB drives, check sysfs speed
        if (busType == CoreBusType.Usb)
        {
            try
            {
                var deviceName = Path.GetFileName(devicePath);
                var speedPath = $"/sys/block/{deviceName}/device/speed";
                if (File.Exists(speedPath))
                {
                    var content = (await File.ReadAllTextAsync(speedPath, cancellationToken)).Trim();
                    if (int.TryParse(content, out var speed))
                    {
                        if (speed >= 5000)
                            return (speed, $"USB 3.x SuperSpeed ({speed} Mbps)");
                        if (speed >= 480)
                            return (speed, $"USB 2.0 Hi-Speed ({speed} Mbps)");
                        return (speed, $"USB ({speed} Mbps)");
                    }
                }
            }
            catch { }
            return (480, "USB 2.0 Hi-Speed (480 Mbps) – předpokládáno");
        }

        if (busType == CoreBusType.Sata)
        {
            try
            {
                var deviceName = Path.GetFileName(devicePath);
                var sataSpeedPath = $"/sys/class/ata_link/{deviceName}/sata_spd";
                if (File.Exists(sataSpeedPath))
                {
                    var content = (await File.ReadAllTextAsync(sataSpeedPath, cancellationToken)).Trim();
                    if (int.TryParse(content, out var gen))
                    {
                        var speed = gen switch { 1 => 1500, 2 => 3000, 3 => 6000, _ => 6000 };
                        return (speed, $"SATA {gen} ({speed} Mbps)");
                    }
                }
            }
            catch { }
            return (6000, "SATA 6 Gb/s (6 000 Mbps)");
        }

        if (busType == CoreBusType.Sas)
            return (12000, "SAS 12 Gb/s (12 000 Mbps)");

        if (busType == CoreBusType.Ide)
            return (133, "IDE ATA-133 (133 Mbps)");

        return (null, null);
    }
}