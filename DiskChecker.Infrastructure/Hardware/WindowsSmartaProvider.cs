using System.Diagnostics;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

public class WindowsSmartaProvider : ISmartaProvider
{
    /// <inheritdoc />
    public async Task<SmartaData?> GetSmartaDataAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var powershellScript = $@"
$drive = Get-PhysicalDisk -DeviceNumber {GetDriveNumber(drivePath)} 2>$null
if ($drive) {{
    $info = $drive | Select-Object Model, SerialNumber, FirmwareVersion | ConvertTo-Json -Compress
    $attrs = (Get-StorageHealthReport -EntityPhysicalDisk $drive.UniqueId 2>$null).Attributes | ConvertTo-Json -Compress
    Write-Output $info
    Write-Output '__ATTRS__'
    Write-Output $attrs
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

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var sections = output.Split("__ATTRS__", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (sections.Length == 0)
            {
                return null;
            }

            var diskInfoJson = sections[0];
            var attributesJson = sections.Length > 1 ? sections[1] : string.Empty;

            return WindowsSmartJsonParser.Parse(diskInfoJson, attributesJson);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<CoreDriveInfo>> ListDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<CoreDriveInfo>();

        var powershellScript = @"
Get-PhysicalDisk | Select-Object DeviceNumber, DeviceID, Model, SerialNumber, Size, MediaType";

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

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5)
            {
                drives.Add(new CoreDriveInfo
                {
                    Path = $@"\\.\PhysicalDrive{parts[0].Trim()}",
                    Name = parts[2].Trim(),
                    TotalSize = long.TryParse(parts[4].Trim(), out var size) ? size : 0
                });
            }
        }
        
        return drives;
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
