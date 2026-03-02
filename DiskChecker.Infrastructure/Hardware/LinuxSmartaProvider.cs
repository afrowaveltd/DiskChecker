using System.Diagnostics;
using System.Text.RegularExpressions;
using System.ComponentModel;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

public class LinuxSmartaProvider : ISmartaProvider, IAdvancedSmartaProvider
{
    private bool? _isSmartctlInstalled;

    /// <inheritdoc />
    public async Task<SmartaData?> GetSmartaDataAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        if (_isSmartctlInstalled == false)
        {
            return null;
        }

        try
        {
            var data = await GetSmartctlDataAsync(drivePath, cancellationToken);
            if (data == null)
            {
                // Verify if it's missing or just failed
                await CheckDependenciesAsync(cancellationToken);
            }
            return data;
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
            // Use lsblk for much better info on Linux
            var psi = new ProcessStartInfo
            {
                FileName = "lsblk",
                Arguments = "-d -n -o NAME,PATH,SIZE,MODEL,SERIAL -b -J",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return drives;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(output))
            {
                return await ListDrivesFallbackAsync(cancellationToken);
            }

            // Simple parsing if no JSON, but we requested -J
            // Let's assume for now we might not have -J on all distros or old versions
            // Try to parse JSON first
            try {
                using var doc = System.Text.Json.JsonDocument.Parse(output);
                if (doc.RootElement.TryGetProperty("blockdevices", out var devices)) {
                    foreach (var dev in devices.EnumerateArray()) {
                        drives.Add(new CoreDriveInfo {
                            Path = dev.GetProperty("path").GetString() ?? $"/dev/{dev.GetProperty("name").GetString()}",
                            Name = dev.GetProperty("model").GetString() ?? dev.GetProperty("name").GetString() ?? "Unknown",
                            TotalSize = dev.GetProperty("size").ValueKind == System.Text.Json.JsonValueKind.Number 
                                ? dev.GetProperty("size").GetInt64() 
                                : long.TryParse(dev.GetProperty("size").GetString(), out var s) ? s : 0
                        });
                    }
                    return drives;
                }
            } catch { }

            return await ListDrivesFallbackAsync(cancellationToken);
        }
        catch
        {
            return await ListDrivesFallbackAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<CoreDriveInfo>> ListDrivesFallbackAsync(CancellationToken cancellationToken)
    {
        var drives = new List<CoreDriveInfo>();
        var psi = new ProcessStartInfo
        {
            FileName = "bash",
            Arguments = "-c \"ls /dev/sd* /dev/nvme* 2>/dev/null\"",
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
            drives.Add(new CoreDriveInfo
            {
                Path = line,
                Name = line.Split('/').Last(),
                TotalSize = 0
            });
        }
        return drives;
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

            // smartctl returns non-zero even for success if there are warnings
            if (string.IsNullOrWhiteSpace(output))
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

    /// <inheritdoc />
    public async Task<string?> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default)
    {
        await CheckDependenciesAsync(cancellationToken);
        if (_isSmartctlInstalled == true)
        {
            return null;
        }

        var distro = await GetDistroInfoAsync(cancellationToken);
        if (distro.Contains("debian", StringComparison.OrdinalIgnoreCase) || distro.Contains("ubuntu", StringComparison.OrdinalIgnoreCase))
        {
            return "Balíček 'smartmontools' nebyl nalezen. Nainstalujte jej příkazem:\n  [yellow]sudo apt-get update && sudo apt-get install -y smartmontools[/]";
        }
        else if (distro.Contains("alma", StringComparison.OrdinalIgnoreCase) || distro.Contains("rhel", StringComparison.OrdinalIgnoreCase) || distro.Contains("centos", StringComparison.OrdinalIgnoreCase))
        {
            return "Balíček 'smartmontools' nebyl nalezen. Nainstalujte jej příkazem:\n  [yellow]sudo dnf install -y smartmontools[/]";
        }
        
        return "Balíček 'smartmontools' (obsahující smartctl) nebyl nalezen. Nainstalujte jej prosím pro váš Linuxový systém.";
    }

    /// <inheritdoc />
    public async Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default)
    {
        var distro = await GetDistroInfoAsync(cancellationToken);
        string command;
        string args;

        if (distro.Contains("debian", StringComparison.OrdinalIgnoreCase) || distro.Contains("ubuntu", StringComparison.OrdinalIgnoreCase))
        {
            command = "sudo";
            args = "apt-get update && sudo apt-get install -y smartmontools";
        }
        else if (distro.Contains("alma", StringComparison.OrdinalIgnoreCase) || distro.Contains("rhel", StringComparison.OrdinalIgnoreCase) || distro.Contains("centos", StringComparison.OrdinalIgnoreCase))
        {
            command = "sudo";
            args = "dnf install -y smartmontools";
        }
        else
        {
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"{command} {args}\"",
                UseShellExecute = false,
                CreateNoWindow = false
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync(cancellationToken);
            _isSmartctlInstalled = process.ExitCode == 0;
            return _isSmartctlInstalled.Value;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GetDistroInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists("/etc/os-release"))
            {
                return await File.ReadAllTextAsync("/etc/os-release", cancellationToken);
            }
        }
        catch { }
        return "unknown";
    }

    private async Task CheckDependenciesAsync(CancellationToken cancellationToken)
    {
        if (_isSmartctlInstalled.HasValue) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"command -v smartctl\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                _isSmartctlInstalled = !string.IsNullOrWhiteSpace(output);
            }
        }
        catch
        {
            _isSmartctlInstalled = false;
        }
    }

    /// <inheritdoc />
    public async Task<int?> GetTemperatureOnlyAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        // On Linux, we can use smartctl with -A flag to get just attributes (faster)
        // Or parse /sys/class/hwmon if available
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "smartctl",
                Arguments = $"-A {drivePath} --json=c",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(output))
                return null;

            var data = SmartctlJsonParser.Parse(output);
            return data?.Temperature > 0 ? (int)Math.Round(data.Temperature, MidpointRounding.AwayFromZero) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SmartaAttributeItem>> GetSmartAttributesAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        var json = await RunSmartctlJsonAsync(drivePath, "-a", cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? Array.Empty<SmartaAttributeItem>() : SmartctlJsonParser.ParseAttributes(json);
    }

    /// <inheritdoc />
    public async Task<bool> StartSelfTestAsync(string drivePath, SmartaSelfTestType selfTestType, CancellationToken cancellationToken = default)
    {
        var testArg = selfTestType switch
        {
            SmartaSelfTestType.Quick => "short",
            SmartaSelfTestType.Extended => "long",
            SmartaSelfTestType.Conveyance => "conveyance",
            SmartaSelfTestType.Selective => "selective",
            SmartaSelfTestType.Offline => "offline",
            SmartaSelfTestType.Abort => "abort",
            _ => "short"
        };

        var psi = new ProcessStartInfo
        {
            FileName = "smartctl",
            Arguments = $"-t {testArg} \"{drivePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode is 0 or 4 || output.Contains("Please wait", StringComparison.OrdinalIgnoreCase) || errors.Contains("Please wait", StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<SmartaSelfTestStatus?> GetSelfTestStatusAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        var json = await RunSmartctlJsonAsync(drivePath, "-a", cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? null : SmartctlJsonParser.ParseSelfTestStatus(json);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SmartaSelfTestEntry>> GetSelfTestLogAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        var json = await RunSmartctlJsonAsync(drivePath, "-a", cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? Array.Empty<SmartaSelfTestEntry>() : SmartctlJsonParser.ParseSelfTestLog(json);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SmartaMaintenanceAction>> GetSupportedMaintenanceActionsAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SmartaMaintenanceAction> actions =
        [
            SmartaMaintenanceAction.EnableSmart,
            SmartaMaintenanceAction.DisableSmart,
            SmartaMaintenanceAction.EnableAutoSave,
            SmartaMaintenanceAction.DisableAutoSave,
            SmartaMaintenanceAction.RunOfflineDataCollection,
            SmartaMaintenanceAction.AbortSelfTest
        ];
        return Task.FromResult(actions);
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteMaintenanceActionAsync(string drivePath, SmartaMaintenanceAction action, CancellationToken cancellationToken = default)
    {
        var arguments = action switch
        {
            SmartaMaintenanceAction.EnableSmart => "-s on",
            SmartaMaintenanceAction.DisableSmart => "-s off",
            SmartaMaintenanceAction.EnableAutoSave => "-S on",
            SmartaMaintenanceAction.DisableAutoSave => "-S off",
            SmartaMaintenanceAction.RunOfflineDataCollection => "-t offline",
            SmartaMaintenanceAction.AbortSelfTest => "-X",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "smartctl",
            Arguments = $"{arguments} \"{drivePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode is 0 or 4;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static async Task<string?> RunSmartctlJsonAsync(string drivePath, string commandArgs, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "smartctl",
            Arguments = $"-j {commandArgs} \"{drivePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
