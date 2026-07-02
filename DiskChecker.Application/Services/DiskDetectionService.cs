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
    private readonly ISmartaProvider? _smartaProvider;

    /// <summary>
    /// Creates a new DiskDetectionService.
    /// When smartaProvider is provided, SMART support is probed during
    /// initial disk detection and stored in CoreDriveInfo.SupportsSmart.
    /// </summary>
    public DiskDetectionService(ISmartaProvider? smartaProvider = null)
    {
        _smartaProvider = smartaProvider;
    }

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

        // Detect connection speeds for all physical drives (non-blocking, best-effort)
        _ = Task.Run(async () =>
        {
            foreach (var drive in drives.Where(d => d.IsPhysical))
            {
                try
                {
                    var (speedMbps, speedDesc) = await DetectConnectionSpeedAsync(drive.Path, drive.BusType, CancellationToken.None);
                    drive.ConnectionSpeedMbps = speedMbps;
                    drive.ConnectionSpeedDescription = speedDesc;
                }
                catch
                {
                    // Non-critical
                }
            }
        }, CancellationToken.None);
        
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

        // Probe SMART support for each physical drive (best-effort).
        // This populates the ISmartaProvider cache so that subsequent SMART queries
        // know immediately whether the device supports SMART without re-running smartctl.
        if (_smartaProvider != null)
        {
            foreach (var drive in drives.Where(d => d.IsPhysical))
            {
                try
                {
                    using var perDiskCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, perDiskCts.Token);
                    var supportsSmart = await _smartaProvider.IsSmartSupportedAsync(drive.Path, linkedCts.Token);
                    drive.SupportsSmart = supportsSmart;
                }
                catch
                {
                    drive.SupportsSmart = false;
                }
            }
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

    /// <summary>
    /// Detects the connection speed (Mbps) and human-readable description for a drive.
    /// Uses platform-specific methods: PowerShell/WMI on Windows, /sys/block on Linux.
    /// </summary>
    public async Task<(int? SpeedMbps, string? Description)> DetectConnectionSpeedAsync(string devicePath, CoreBusType busType, CancellationToken cancellationToken = default)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await DetectConnectionSpeedWindowsAsync(devicePath, busType, cancellationToken);
        }
        else
        {
            return await DetectConnectionSpeedLinuxAsync(devicePath, busType, cancellationToken);
        }
    }

    private async Task<(int? SpeedMbps, string? Description)> DetectConnectionSpeedWindowsAsync(string devicePath, CoreBusType busType, CancellationToken cancellationToken)
    {
        // For NVMe, try to get PCIe link speed
        if (busType == CoreBusType.Nvme)
        {
            try
            {
                // Extract physical drive number
                var match = System.Text.RegularExpressions.Regex.Match(devicePath, @"(\d+)");
                if (match.Success)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_DiskDrive | Where-Object {{ $_.DeviceID -like '*{devicePath}*' }} | Select-Object -ExpandProperty InterfaceType\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
                        await process.WaitForExitAsync(cancellationToken);
                        
                        // NVMe drives on Windows typically report PCIe generation in various ways
                        // We'll use a heuristic based on common NVMe speeds
                        return (32000, "NVMe PCIe Gen4 x4 (až 32 000 Mbps, ~7 000 MB/s)");
                    }
                }
            }
            catch { }
            return (32000, "NVMe (až 32 000 Mbps, ~7 000 MB/s)");
        }

        // For USB drives, try to detect USB version via WMI
        if (busType == CoreBusType.Usb)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_USBControllerDevice | Where-Object {{ $_.Dependent -like '*{devicePath.Replace("\\", "\\\\")}*' }} | ForEach-Object {{ $_.Antecedent }} | ForEach-Object {{ Get-CimInstance -InputObject $_ | Select-Object -ExpandProperty Name }}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
                    await process.WaitForExitAsync(cancellationToken);
                    
                    if (output.Contains("3.", StringComparison.OrdinalIgnoreCase) || output.Contains("USB 3", StringComparison.OrdinalIgnoreCase) || output.Contains("SuperSpeed", StringComparison.OrdinalIgnoreCase))
                        return (5000, "USB 3.x SuperSpeed (5 000 Mbps, ~450 MB/s)");
                    if (output.Contains("2.", StringComparison.OrdinalIgnoreCase) || output.Contains("USB 2", StringComparison.OrdinalIgnoreCase) || output.Contains("Hi-Speed", StringComparison.OrdinalIgnoreCase))
                        return (480, "USB 2.0 Hi-Speed (480 Mbps, ~40 MB/s)");
                    if (output.Contains("1.", StringComparison.OrdinalIgnoreCase) || output.Contains("USB 1", StringComparison.OrdinalIgnoreCase))
                        return (12, "USB 1.1 Full-Speed (12 Mbps, ~1 MB/s)");
                }
            }
            catch { }

            // Fallback: try to detect via USB hub properties
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_USBHub | Where-Object { $_.Name -like '*SuperSpeed*' -or $_.Name -like '*3.0*' } | Measure-Object | Select-Object -ExpandProperty Count\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
                    await process.WaitForExitAsync(cancellationToken);
                    if (int.TryParse(output, out var count) && count > 0)
                        return (5000, "USB 3.x SuperSpeed (až 5 000 Mbps)");
                }
            }
            catch { }

            // Default USB assumption: USB 2.0 (most common for external adapters)
            return (480, "USB 2.0 Hi-Speed (480 Mbps) – předpokládáno");
        }

        // SATA
        if (busType == CoreBusType.Sata)
            return (6000, "SATA 6 Gb/s (6 000 Mbps)");

        // SAS
        if (busType == CoreBusType.Sas)
            return (12000, "SAS 12 Gb/s (12 000 Mbps)");

        // IDE
        if (busType == CoreBusType.Ide)
            return (133, "IDE ATA-133 (133 Mbps)");

        return (null, null);
    }

    private async Task<(int? SpeedMbps, string? Description)> DetectConnectionSpeedLinuxAsync(string devicePath, CoreBusType busType, CancellationToken cancellationToken)
    {
        // For NVMe, read PCIe link speed from sysfs
        if (busType == CoreBusType.Nvme)
        {
            try
            {
                // /sys/block/nvme0n1/device/device/link_speed or similar
                var deviceName = Path.GetFileName(devicePath); // e.g., "nvme0n1"
                var nvmeController = deviceName.Replace("n1", "").Replace("p1", ""); // e.g., "nvme0"
                
                var linkSpeedPaths = new[]
                {
                    $"/sys/block/{deviceName}/device/device/link_speed",
                    $"/sys/class/nvme/{nvmeController}/device/link_speed",
                    $"/sys/devices/pci*/*/nvme/{nvmeController}/link_speed"
                };

                foreach (var path in linkSpeedPaths)
                {
                    if (File.Exists(path))
                    {
                        var content = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
                        // Format: "16.0 GT/s PCIe" or "8 GT/s PCIe"
                        var speedMatch = System.Text.RegularExpressions.Regex.Match(content, @"(\d+\.?\d*)\s*GT/s");
                        if (speedMatch.Success && double.TryParse(speedMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var gtPerSec))
                        {
                            // GT/s * lanes * encoding efficiency
                            // Assume x4 lanes, 128b/130b encoding (~98.5% efficiency)
                            var lanes = 4;
                            var encodingEfficiency = 0.985;
                            var rawMbps = gtPerSec * 1000 * lanes * encodingEfficiency;
                            var gen = gtPerSec >= 16 ? "Gen5" : gtPerSec >= 8 ? "Gen4" : gtPerSec >= 4 ? "Gen3" : "Gen2";
                            return ((int)rawMbps, $"NVMe PCIe {gen} x{lanes} ({gtPerSec} GT/s, až {(int)rawMbps} Mbps)");
                        }
                    }
                }

                // Try to read PCIe width
                foreach (var path in linkSpeedPaths.Select(p => p.Replace("link_speed", "link_width")))
                {
                    if (File.Exists(path))
                    {
                        var width = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
                        // Use with default Gen4 assumption
                        if (int.TryParse(width.Replace("x", ""), out var lanes))
                        {
                            return (32000, $"NVMe PCIe Gen4 x{lanes} (až 32 000 Mbps)");
                        }
                    }
                }
            }
            catch { }
            return (32000, "NVMe (až 32 000 Mbps)");
        }

        // For USB drives, check /sys/block/<device>/device/speed or similar
        if (busType == CoreBusType.Usb)
        {
            try
            {
                var deviceName = Path.GetFileName(devicePath); // e.g., "sdb"
                var speedPaths = new[]
                {
                    $"/sys/block/{deviceName}/device/speed",
                    $"/sys/class/block/{deviceName}/device/speed",
                    $"/sys/class/scsi_device/*/device/speed"
                };

                foreach (var path in speedPaths)
                {
                    // Expand wildcards
                    var dir = Path.GetDirectoryName(path);
                    var pattern = Path.GetFileName(path);
                    if (dir != null && pattern != null && Directory.Exists(dir))
                    {
                        var matches = Directory.GetFiles(dir, pattern);
                        foreach (var match in matches)
                        {
                            if (File.Exists(match))
                            {
                                var content = (await File.ReadAllTextAsync(match, cancellationToken)).Trim();
                                if (int.TryParse(content, out var speed))
                                {
                                    // Speed is typically in Mbps or Gbps
                                    if (speed >= 5000)
                                        return (speed, $"USB 3.x SuperSpeed ({speed} Mbps)");
                                    if (speed >= 480)
                                        return (speed, $"USB 2.0 Hi-Speed ({speed} Mbps)");
                                    return (speed, $"USB ({speed} Mbps)");
                                }
                            }
                        }
                    }
                }

                // Try lsusb for USB version
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "lsusb",
                        Arguments = "-v",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                        await process.WaitForExitAsync(cancellationToken);
                        
                        if (output.Contains("bcdUSB", StringComparison.OrdinalIgnoreCase))
                        {
                            var usbVersionMatch = System.Text.RegularExpressions.Regex.Match(output, @"bcdUSB\s+(\d+\.\d+)");
                            if (usbVersionMatch.Success)
                            {
                                var version = usbVersionMatch.Groups[1].Value;
                                if (version.StartsWith("3.", StringComparison.Ordinal))
                                    return (5000, $"USB {version} SuperSpeed (až 5 000 Mbps)");
                                if (version.StartsWith("2.", StringComparison.Ordinal))
                                    return (480, $"USB {version} Hi-Speed (480 Mbps)");
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
            return (480, "USB 2.0 Hi-Speed (480 Mbps) – předpokládáno");
        }

        // SATA - try to read from sysfs
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
                        // gen: 1=1.5Gbps, 2=3Gbps, 3=6Gbps
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

    public async Task<CoreDriveInfo?> GetDriveAsync(string path, CancellationToken cancellationToken = default)
    {
        var drives = await GetDrivesAsync(cancellationToken);
        return drives.FirstOrDefault(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }
}