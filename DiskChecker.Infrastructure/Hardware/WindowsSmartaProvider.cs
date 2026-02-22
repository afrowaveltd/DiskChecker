using System.Diagnostics;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

public class WindowsSmartaProvider : ISmartaProvider
{
    public async Task<SmartaData?> GetSmartaDataAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var smartaData = new SmartaData();
            
            // Get SMART data via powershell
            var powershellScript = $@"
$drive = Get-PhysicalDisk -DeviceNumber {GetDriveNumber(drivePath)} 2>$null
if ($drive) {{
    $drive | Select-Object Model, SerialNumber, FirmwareVersion, MediaType
    Get-StorageHealthReport -EntityPhysicalDisk $drive.UniqueId 2>$null | Select-Object -ExpandProperty attributes
}}";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{powershellScript}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                ParseWindowsSmartaOutput(output, smartaData);
                return smartaData;
            }

            return null;
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
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-PhysicalDisk -DeviceNumber {GetDriveNumber(drivePath)}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return !string.IsNullOrWhiteSpace(output) && !output.Contains("NotFound");
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<CoreDriveInfo>> ListDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<CoreDriveInfo>();

        var powershellScript = @"
Get-PhysicalDisk | Select-Object -Property 
    @{Name='DriveNumber';Expression={$_.DeviceNumber}},
    @{Name='DeviceID';Expression={$_.DeviceID}},
    Model, SerialNumber, 
    @{Name='Size';Expression={$_.Size}},
    @{Name='MediaType';Expression={$_.MediaType}}";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{powershellScript}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return drives;

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        // Parse output and create drive info
        // This is simplified - in production would parse JSON output
        
        return drives;
    }

    private void ParseWindowsSmartaOutput(string output, SmartaData smartaData)
    {
        if (string.IsNullOrWhiteSpace(output)) return;

        // Parse output - simplified for example
        // In production would parse structured JSON or CSV output
    }

    private int GetDriveNumber(string drivePath)
    {
        // Parse drive path like \\.\PhysicalDrive0
        if (int.TryParse(new string(drivePath.Where(char.IsDigit).ToArray()), out var num))
        {
            return num;
        }
        return 0;
    }
}
