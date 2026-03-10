using System.Diagnostics;
using System.Text.Json;
using System.ComponentModel;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Provides SMART data for Windows systems using WMI/PowerShell and smartctl.
/// </summary>
public class WindowsSmartaProvider : ISmartaProvider, IAdvancedSmartaProvider
{
    private readonly ILogger<WindowsSmartaProvider>? _logger;
    private SmartaData? _lastSuccessfulSmartData;

    public WindowsSmartaProvider(ILogger<WindowsSmartaProvider>? logger = null)
    {
        _logger = logger;
    }

    public async Task<SmartaData?> GetSmartaDataAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var smartctlTask = GetSmartaDataViaSmartctlAsync(devicePath, cancellationToken);
        var windowsTask = GetSmartaDataViaPowerShellAsync(devicePath, cancellationToken);

        await Task.WhenAll(smartctlTask, windowsTask);

        var smartctl = await smartctlTask;
        var windows = await windowsTask;

        if (smartctl == null && windows == null) 
        {
            return _lastSuccessfulSmartData;
        }

        var result = smartctl ?? windows;

        if (result != null && _lastSuccessfulSmartData != null)
        {
            if (string.IsNullOrEmpty(result.SerialNumber) || result.SerialNumber == "N/A")
            {
                result.SerialNumber = _lastSuccessfulSmartData.SerialNumber;
            }
            if (string.IsNullOrEmpty(result.DeviceModel))
            {
                result.DeviceModel = _lastSuccessfulSmartData.DeviceModel;
            }
            if (string.IsNullOrEmpty(result.FirmwareVersion))
            {
                result.FirmwareVersion = _lastSuccessfulSmartData.FirmwareVersion;
            }
        }

        if (result != null && !string.IsNullOrEmpty(result.SerialNumber) && result.SerialNumber != "N/A")
        {
            _lastSuccessfulSmartData = result;
        }

        return result;
    }

    public async Task<SmartaData?> GetAdvancedSmartaDataAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        return await GetSmartaDataAsync(devicePath, cancellationToken);
    }

    private async Task<SmartaData?> GetSmartaDataViaPowerShellAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"disk_smart_{Guid.NewGuid():N}.ps1");
            
            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(driveNumber))
            {
                return null;
            }

            var script = $@"$ErrorActionPreference = 'SilentlyContinue'
$driveNum = '{driveNumber}'
$res = @{{
    Model = ''
    SerialNumber = ''
    FirmwareVersion = ''
    Temperature = 0
    PowerOnHours = 0
    ReallocatedSectorCount = 0
    PendingSectorCount = 0
    UncorrectableErrorCount = 0
}}

try {{
    $drive = Get-CimInstance Win32_DiskDrive | Where-Object {{ $_.DeviceID -like ""*\\PhysicalDrive$driveNum"" }}
    if ($drive) {{
        $res.Model = $drive.Model
        $res.FirmwareVersion = $drive.FirmwareRevision
    }}
}} catch {{}}

$res | ConvertTo-Json -Compress";

            try
            {
                await File.WriteAllTextAsync(tempScriptPath, script, cancellationToken);
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return null;
                
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                
                if (string.IsNullOrWhiteSpace(output) || !output.Trim().StartsWith('{'))
                {
                    return null;
                }
                
                return WindowsSmartJsonParser.Parse(output.Trim(), "[]");
            }
            finally
            {
                try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
            }
        }
        catch 
        { 
            return null; 
        }
    }

    public async Task<List<SmartaAttributeItem>> GetSmartAttributesAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var execution = await ExecuteSmartctlCommandAsync(devicePath, "-j -a", cancellationToken);
        if (execution == null || string.IsNullOrWhiteSpace(execution.Value.Output))
        {
            return new List<SmartaAttributeItem>();
        }

        return SmartctlJsonParser.ParseAttributes(execution.Value.Output).ToList();
    }

    public async Task<bool> StartSelfTestAsync(string devicePath, SmartaSelfTestType testType, CancellationToken cancellationToken = default)
    {
        var smartTestArgument = testType switch
        {
            SmartaSelfTestType.Quick => "short",
            SmartaSelfTestType.Extended => "long",
            SmartaSelfTestType.Conveyance => "conveyance",
            SmartaSelfTestType.Selective => "selective",
            SmartaSelfTestType.Offline => "offline",
            SmartaSelfTestType.Abort => "abort",
            _ => "short"
        };

        var execution = await ExecuteSmartctlCommandAsync(devicePath, $"-t {smartTestArgument}", cancellationToken);
        if (execution == null) return false;

        return execution.Value.ExitCode is 0 or 4
            || execution.Value.Output.Contains("Please wait", StringComparison.OrdinalIgnoreCase)
            || execution.Value.Error.Contains("Please wait", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SmartaSelfTestStatus> GetSelfTestStatusAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var execution = await ExecuteSmartctlCommandAsync(devicePath, "-j -a", cancellationToken);
        if (execution == null || string.IsNullOrWhiteSpace(execution.Value.Output))
        {
            return SmartaSelfTestStatus.Unknown;
        }

        var result = SmartctlJsonParser.Parse(execution.Value.Output);
        if (result?.CurrentSelfTest != null)
        {
            return result.CurrentSelfTest.Status;
        }
        return SmartaSelfTestStatus.Unknown;
    }

    public async Task<List<SmartaSelfTestEntry>> GetSelfTestLogAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var execution = await ExecuteSmartctlCommandAsync(devicePath, "-j -a", cancellationToken);
        if (execution == null || string.IsNullOrWhiteSpace(execution.Value.Output))
        {
            return new List<SmartaSelfTestEntry>();
        }

        return SmartctlJsonParser.ParseSelfTestLog(execution.Value.Output).ToList();
    }

    public Task<List<SmartaMaintenanceAction>> GetSupportedMaintenanceActionsAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        // The enum only has None, Backup, Replace - no smartctl actions
        return Task.FromResult(new List<SmartaMaintenanceAction>());
    }

    public async Task<bool> ExecuteMaintenanceActionAsync(string devicePath, SmartaMaintenanceAction action, CancellationToken cancellationToken = default)
    {
        // No smartctl maintenance actions defined in the current enum
        await Task.CompletedTask;
        return false;
    }

    private async Task<(int ExitCode, string Output, string Error)?> ExecuteSmartctlCommandAsync(string devicePath, string commandArguments, CancellationToken cancellationToken)
    {
        try
        {
            var path = await FindSmartctlPathAsync();
            if (string.IsNullOrEmpty(path)) return null;

            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            if (string.IsNullOrWhiteSpace(driveNumber) || !int.TryParse(driveNumber, out var driveIndex))
            {
                return null;
            }

            // On Windows, use /dev/pdN format for smartctl
            var devPath = $"/dev/pd{driveIndex}";
            var args = $"{commandArguments} {devPath}";

            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, output, error);
        }
        catch
        {
            return null;
        }
    }

    private async Task<SmartaData?> GetSmartaDataViaSmartctlAsync(string devicePath, CancellationToken cancellationToken)
    {
        return await ExecuteSmartctlForSmartDataAsync(devicePath, cancellationToken);
    }

    private async Task<SmartaData?> ExecuteSmartctlForSmartDataAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            var path = await FindSmartctlPathAsync();
            if (string.IsNullOrEmpty(path)) return null;

            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(driveNumber)) return null;

            // On Windows, use /dev/pdN format for smartctl
            var devPath = $"/dev/pd{driveNumber}";
            var args = $"-j -a {devPath}";

            var psi = new ProcessStartInfo 
            { 
                FileName = path, 
                Arguments = args, 
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false, 
                CreateNoWindow = true 
            };
            
            using var process = Process.Start(psi);
            if (process == null) return null;
            
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(output)) return null;
            
            var result = SmartctlJsonParser.Parse(output);
            if (result == null) return null;
            
            return SmartctlJsonParser.ToSmartaData(result);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> FindSmartctlPathAsync()
    {
        if (await IsSmartctlInPathAsync()) return "smartctl";

        var common = new[] 
        { 
            @"C:\Program Files\smartmontools\bin\smartctl.exe", 
            @"C:\Program Files (x86)\smartmontools\bin\smartctl.exe",
            @"C:\ProgramData\chocolatey\bin\smartctl.exe",
            @"C:\tools\smartmontools\smartctl.exe"
        };

        return common.FirstOrDefault(File.Exists);
    }

    private async Task<bool> IsSmartctlInPathAsync()
    {
        try
        {
            var psi = new ProcessStartInfo 
            { 
                FileName = "where", 
                Arguments = "smartctl", 
                RedirectStandardOutput = true, 
                UseShellExecute = false, 
                CreateNoWindow = true 
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task<bool> IsDriveValidAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var drives = await ListDrivesAsync(cancellationToken);
        return drives.Any(d => d == devicePath);
    }

    public async Task<List<string>> ListDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<string>();
        
        try
        {
            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"list_drives_{Guid.NewGuid():N}.ps1");
            
            try
            {
                var simpleScript = @"$result = @()
if (Get-Command Get-Disk -ErrorAction SilentlyContinue) {
    Get-Disk | ForEach-Object {
        $result += ""\\\\.\\PhysicalDrive$($_.Number)""
    }
}
if ($result.Count -eq 0) {
    Get-CimInstance Win32_DiskDrive | ForEach-Object {
        $result += $_.DeviceID
    }
}
$result | ConvertTo-Json";

                await File.WriteAllTextAsync(tempScriptPath, simpleScript, cancellationToken);
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
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
                        var deviceId = item.GetString();
                        if (!string.IsNullOrEmpty(deviceId))
                        {
                            drives.Add(deviceId);
                        }
                    }
                }
                else if (root.ValueKind == JsonValueKind.String)
                {
                    var deviceId = root.GetString();
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        drives.Add(deviceId);
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
            }
        }
        catch { }

        return drives;
    }

    public async Task<string> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default)
    {
        if (await FindSmartctlPathAsync() != null) return string.Empty;
        return "Nainstalujte: winget install smartmontools";
    }

    public async Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo 
            { 
                FileName = "winget", 
                Arguments = "install --id smartmontools.smartmontools --silent --accept-package-agreements --accept-source-agreements", 
                UseShellExecute = false, 
                CreateNoWindow = false 
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task<int?> GetTemperatureOnlyAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var data = await GetSmartaDataAsync(devicePath, cancellationToken);
        return data?.Temperature > 0 ? data.Temperature : null;
    }
}