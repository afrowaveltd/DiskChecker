using System.Diagnostics;
using System.Text.Json;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Provides SMART data for Windows systems using WMI/PowerShell and smartctl.
/// </summary>
public class WindowsSmartaProvider : ISmartaProvider
{
    private readonly ILogger<WindowsSmartaProvider>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsSmartaProvider"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public WindowsSmartaProvider(ILogger<WindowsSmartaProvider>? logger = null)
    {
        _logger = logger;
    }

    public async Task<SmartaData?> GetSmartaDataAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        // 1. Get data from all sources in parallel
        var smartctlTask = GetSmartaDataViaSmartctlAsync(drivePath, cancellationToken);
        var windowsTask = GetSmartaDataViaPowerShellAsync(drivePath, cancellationToken);

        await Task.WhenAll(smartctlTask, windowsTask);

        var smartctl = await smartctlTask;
        var windows = await windowsTask;

        if (smartctl == null && windows == null) return null;

        // 2. Build combined result (Merge)
        // Priority: smartctl has more detailed data, so use it when available
        var result = smartctl ?? windows!;

        // 3. If both exist, merge them (smartctl takes priority for detailed SMART metrics, Windows for basic info)
        if (smartctl != null && windows != null)
        {
            // Use Windows data for model info if better
            if (string.IsNullOrEmpty(result.DeviceModel) && !string.IsNullOrEmpty(windows.DeviceModel))
                result.DeviceModel = windows.DeviceModel;
            if (string.IsNullOrEmpty(result.SerialNumber) && !string.IsNullOrEmpty(windows.SerialNumber))
                result.SerialNumber = windows.SerialNumber;
            if (string.IsNullOrEmpty(result.FirmwareVersion) && !string.IsNullOrEmpty(windows.FirmwareVersion))
                result.FirmwareVersion = windows.FirmwareVersion;
            
            // Use highest temperature value
            if (windows.Temperature > result.Temperature)
                result.Temperature = windows.Temperature;
            if (windows.PowerOnHours > result.PowerOnHours)
                result.PowerOnHours = windows.PowerOnHours;
        }

        return result;
    }

    private string? PickNotEmpty(string? s1, string? s2)
    {
        if (!string.IsNullOrWhiteSpace(s1) && s1 != "---") return s1;
        return !string.IsNullOrWhiteSpace(s2) && s2 != "---" ? s2 : null;
    }

    private async Task<SmartaData?> GetSmartaDataViaPowerShellAsync(string drivePath, CancellationToken cancellationToken)
    {
        try
        {
            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"disk_smart_{Guid.NewGuid():N}.ps1");
            
            var driveNumber = new string(drivePath.Where(char.IsDigit).ToArray());
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
    # Get basic drive info from WMI
    $drive = Get-CimInstance Win32_DiskDrive | Where-Object {{ $_.DeviceID -like ""*\PhysicalDrive$driveNum"" }}
    if ($drive) {{
        $res.Model = $drive.Model
        $res.SerialNumber = $drive.SerialNumber
        $res.FirmwareVersion = $drive.FirmwareRevision
    }}
}} catch {{}}

try {{
    # Try to get SMART data via WMI (for some drives)
    $smartData = Get-CimInstance MSStorageDriver_FailurePredictData -Namespace root\wmi | Where-Object {{ $_.InstanceName -like ""*PhysicalDrive$driveNum*"" }}
    if ($smartData) {{
        # Parse WMI SMART data if available
        if ($smartData.Data) {{
            # This is complex, skip for now
        }}
    }}
}} catch {{}}

try {{
    # Get physical disk info and reliability counter
    $physicalDisks = @(Get-PhysicalDisk)
    foreach ($pd in $physicalDisks) {{
        if ($pd.DeviceId -eq $driveNum) {{
            $rel = Get-StorageReliabilityCounter -PhysicalDisk $pd -ErrorAction SilentlyContinue
            if ($rel) {{
                if ($rel.Temperature -and $rel.Temperature -gt 0) {{ $res.Temperature = [int]$rel.Temperature }}
                if ($rel.PowerOnHours -and $rel.PowerOnHours -gt 0) {{ $res.PowerOnHours = [int]$rel.PowerOnHours }}
                if ($rel.ReadErrorsUncorrected) {{ $res.UncorrectableErrorCount = [int]$rel.ReadErrorsUncorrected }}
            }}
            break
        }}
    }}
}} catch {{}}

# Return JSON
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
                
                if (string.IsNullOrWhiteSpace(output))
                {
                    return null;
                }
                
                output = output.Trim();
                if (!output.StartsWith('{') || !output.EndsWith('}'))
                {
                    return null;
                }
                
                return WindowsSmartJsonParser.Parse(output, "[]");
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

    private async Task<SmartaData?> GetSmartaDataViaSmartctlAsync(string drivePath, CancellationToken cancellationToken)
    {
        var data = await ExecuteSmartctlAsync(drivePath, null, cancellationToken);
        if (IsDataEmpty(data)) data = await ExecuteSmartctlAsync(drivePath, "sat", cancellationToken);
        return data;
    }

    private bool IsDataEmpty(SmartaData? d) => d == null || (d.Temperature <= 0 && d.PowerOnHours <= 0 && string.IsNullOrEmpty(d.DeviceModel));

    private async Task<SmartaData?> ExecuteSmartctlAsync(string drivePath, string? deviceType, CancellationToken cancellationToken)
    {
        try
        {
            var path = await FindSmartctlPathAsync();
            if (string.IsNullOrEmpty(path)) return null;

            var args = $"--json=a -a {drivePath}";
            if (!string.IsNullOrEmpty(deviceType))
            {
                args = $"-d {deviceType} {args}";
            }

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

            return string.IsNullOrWhiteSpace(output) ? null : SmartctlJsonParser.Parse(output);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> FindSmartctlPathAsync()
    {
        if (await IsSmartctlInPathAsync())
        {
            return "smartctl";
        }

        var common = new[] 
        { 
            @"C:\Program Files\smartmontools\bin\smartctl.exe", 
            @"C:\Program Files (x86)\smartmontools\bin\smartctl.exe",
            @"C:\ProgramData\chocolatey\bin\smartctl.exe",
            @"C:\tools\smartmontools\smartctl.exe"
        };

        return common.FirstOrDefault(File.Exists);
    }

    public async Task<bool> IsDriveValidAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        var drives = await ListDrivesAsync(cancellationToken);
        return drives.Any(d => d.Path == drivePath);
    }

    public async Task<IReadOnlyList<CoreDriveInfo>> ListDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<CoreDriveInfo>();
        
        try
        {
            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"list_drives_{Guid.NewGuid():N}.ps1");
            
            try
            {
                // Ultra-simple script that always works with PowerShell 5.1
                var simpleScript = @"$result = @()
Get-CimInstance Win32_DiskDrive | ForEach-Object {
    $result += [PSCustomObject]@{
        DeviceID = $_.DeviceID
        Model = $_.Model
        Size = $_.Size
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
                
                if (string.IsNullOrWhiteSpace(output))
                {
                    return drives;
                }
                
                // Try to parse JSON output
                try
                {
                    using var doc = JsonDocument.Parse(output.Trim());
                    var root = doc.RootElement;
                    
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            ExtractDriveFromJson(item, drives);
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        ExtractDriveFromJson(root, drives);
                    }
                }
                catch (JsonException)
                {
                    // Silently ignore JSON parse errors
                }
            }
            finally
            {
                try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
            }
        }
        catch
        {
            // Silently handle errors
        }
        
        return drives;
    }

    private static void ExtractDriveFromJson(JsonElement item, List<CoreDriveInfo> drives)
    {
        if (item.TryGetProperty("DeviceID", out var deviceIdProp) &&
            item.TryGetProperty("Model", out var modelProp) &&
            item.TryGetProperty("Size", out var sizeProp))
        {
            var deviceId = deviceIdProp.GetString() ?? "";
            var model = modelProp.GetString() ?? "Unknown";
            if (sizeProp.TryGetInt64(out var size))
            {
                drives.Add(new CoreDriveInfo 
                { 
                    Path = deviceId, 
                    Name = model,
                    TotalSize = size
                });
            }
        }
    }

    public async Task<string?> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default)
    {
        if (await FindSmartctlPathAsync() != null) return null;
        return "Doporučujeme: [yellow]winget install smartmontools[/]";
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
            return process.ExitCode == 0 || (uint)process.ExitCode == 0x8A150039;
        }
        catch { return false; }
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

    private int GetDriveNumber(string drivePath) => int.TryParse(new string(drivePath.Where(char.IsDigit).ToArray()), out var n) ? n : 0;
}
