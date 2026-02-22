using System.Diagnostics;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

public class LinuxSmartaProvider : ISmartaProvider
{
    public async Task<SmartaData?> GetSmartaDataAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var smartaData = new SmartaData();
            
            // Try smartctl first
            var smartctlData = await GetSmartctlDataAsync(drivePath, cancellationToken);
            if (smartctlData != null)
            {
                smartaData = smartctlData;
            }
            
            return smartaData;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsDriveValidAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"ls {drivePath} 2>/dev/null && echo exists\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return output.Contains("exists");
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<CoreDriveInfo>> ListDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<CoreDriveInfo>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"ls /dev/sd* /dev/nvme* 2>/dev/null | head -20\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return drives;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                // TODO: Get more details about each drive
                drives.Add(new CoreDriveInfo
                {
                    Path = line,
                    Name = line.Split('/').Last(),
                    TotalSize = 0,
                    FreeSpace = 0,
                    FileSystem = ""
                });
            }

            return drives;
        }
        catch
        {
            return drives;
        }
    }

    private async Task<SmartaData?> GetSmartctlDataAsync(string drivePath, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "smartctl",
                Arguments = $"-i -A {drivePath}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return null;
            }

            return ParseSmartctlOutput(output);
        }
        catch
        {
            return null;
        }
    }

    private SmartaData ParseSmartctlOutput(string output)
    {
        var smartaData = new SmartaData();
        
        if (string.IsNullOrWhiteSpace(output)) return smartaData;

        // Parse output - simplified for example
        // In production would parse structured SMART data
        // Example format:
        // Model Number:       Samsung SSD 860 EVO 500GB
        // Serial Number:      S3ZJNF0K500000
        // Firmware Version:   RVT1
        // User Capacity:      500,107,862,016 bytes [500 GB]
        // SMART support is:   Enabled
        
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("Model Number:"))
            {
                smartaData.DeviceModel = line.Split(':')[1].Trim();
            }
            else if (line.Contains("Serial Number:"))
            {
                smartaData.SerialNumber = line.Split(':')[1].Trim();
            }
            else if (line.Contains("Firmware Version:"))
            {
                smartaData.FirmwareVersion = line.Split(':')[1].Trim();
            }
        }

        return smartaData;
    }
}
