using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services;

/// <summary>
/// Service for detecting and listing disk drives.
/// </summary>
public class DiskDetectionService : IDiskDetectionService
{
    public async Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<CoreDriveInfo>();
        
        // Get physical drives on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var physicalDrives = await GetPhysicalDrivesWindowsAsync(cancellationToken);
            drives.AddRange(physicalDrives);
        }
        
        // Get logical drives
        var logicalDrives = await GetLogicalDrivesAsync(cancellationToken);
        
        // Merge logical drive info with physical drives where possible
        foreach (var logical in logicalDrives)
        {
            // Don't add duplicates - physical drives already added
            var existing = drives.FirstOrDefault(d => 
                d.Path.Equals(logical.Path, StringComparison.OrdinalIgnoreCase));
            
            if (existing == null)
            {
                drives.Add(logical);
            }
        }
        
        return drives.AsReadOnly();
    }

    private async Task<List<CoreDriveInfo>> GetPhysicalDrivesWindowsAsync(CancellationToken cancellationToken)
    {
        var drives = new List<CoreDriveInfo>();
        
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_DiskDrive | Select-Object DeviceID, Model, Size, MediaType, FirmwareRevision, SerialNumber | ConvertTo-Json -Compress\"",
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
            
            // Handle both single object and array JSON
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
            // Fallback: try WMI directly
            try
            {
                drives = await GetPhysicalDrivesWmiAsync(cancellationToken);
            }
            catch
            {
                // Last resort: just add PHYSICALDRIVE0
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
            Arguments = "diskdrive get deviceid,model,size /format:list",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        using var process = Process.Start(psi);
        if (process == null) return drives;
        
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        
        // Parse WMIC output
        var lines = output.Split('\n');
        string? currentDeviceId = null;
        string? currentModel = null;
        long currentSize = 0;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) 
            {
                // End of record - add drive if we have data
                if (!string.IsNullOrEmpty(currentDeviceId))
                {
                    drives.Add(new CoreDriveInfo
                    {
                        Id = Guid.NewGuid(),
                        Path = currentDeviceId,
                        Name = currentModel ?? "Unknown Drive",
                        TotalSize = currentSize,
                        IsPhysical = true,
                        IsReady = true
                    });
                    currentDeviceId = null;
                    currentModel = null;
                    currentSize = 0;
                }
                continue;
            }
            
            if (trimmed.StartsWith("DeviceID=", StringComparison.OrdinalIgnoreCase))
                currentDeviceId = trimmed.AsSpan(9).ToString();
            else if (trimmed.StartsWith("Model=", StringComparison.OrdinalIgnoreCase))
                currentModel = trimmed.AsSpan(6).ToString();
            else if (trimmed.StartsWith("Size=", StringComparison.OrdinalIgnoreCase) && long.TryParse(trimmed.AsSpan(5), out var size))
                currentSize = size;
        }
        
        // Add last record if any
        if (!string.IsNullOrEmpty(currentDeviceId))
        {
            drives.Add(new CoreDriveInfo
            {
                Id = Guid.NewGuid(),
                Path = currentDeviceId,
                Name = currentModel ?? "Unknown Drive",
                TotalSize = currentSize,
                IsPhysical = true,
                IsReady = true
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
            
            var size = item.TryGetProperty("Size", out var sizeProp) && sizeProp.ValueKind == System.Text.Json.JsonValueKind.Number
                ? sizeProp.GetInt64() 
                : 0;
            
            return new CoreDriveInfo
            {
                Id = Guid.NewGuid(),
                Path = deviceId,
                Name = model ?? "Unknown Drive",
                TotalSize = size,
                IsPhysical = true,
                IsReady = true
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<CoreDriveInfo>> GetLogicalDrivesAsync(CancellationToken cancellationToken)
    {
        var drives = new List<CoreDriveInfo>();
        
        await Task.Run(() =>
        {
            var logicalDrives = DriveInfo.GetDrives();
            
            foreach (var drive in logicalDrives)
            {
                try
                {
                    if (drive.IsReady)
                    {
                        drives.Add(new CoreDriveInfo
                        {
                            Id = Guid.NewGuid(),
                            Path = drive.Name,
                            Name = drive.VolumeLabel ?? drive.Name,
                            TotalSize = drive.TotalSize,
                            FreeSpace = drive.AvailableFreeSpace,
                            FileSystem = drive.DriveFormat,
                            IsPhysical = false,
                            IsReady = drive.IsReady,
                            IsRemovable = drive.DriveType == DriveType.Removable
                        });
                    }
                }
                catch
                {
                    // Skip drives that can't be accessed
                }
            }
        }, cancellationToken);
        
        return drives;
    }

    public async Task<CoreDriveInfo?> GetDriveAsync(string path, CancellationToken cancellationToken = default)
    {
        var drives = await GetDrivesAsync(cancellationToken);
        return drives.FirstOrDefault(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    }
}