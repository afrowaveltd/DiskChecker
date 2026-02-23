using System.Diagnostics;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

public class LinuxSmartaProvider : ISmartaProvider
{
    /// <inheritdoc />
    public async Task<SmartaData?> GetSmartaDataAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetSmartctlDataAsync(drivePath, cancellationToken);
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

    /// <inheritdoc />
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
                Arguments = $"-j -a \"{drivePath}\"",
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

            return SmartctlJsonParser.Parse(output);
        }
        catch
        {
            return null;
        }
    }
}
