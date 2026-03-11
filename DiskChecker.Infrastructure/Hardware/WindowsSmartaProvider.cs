using System.Diagnostics;
using System.Text.Json;
using System.ComponentModel;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Options;
using DiskChecker.Infrastructure.Configuration;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Provides SMART data for Windows systems using WMI/PowerShell and smartctl.
/// </summary>
public class WindowsSmartaProvider : ISmartaProvider, IAdvancedSmartaProvider
{
    private readonly ILogger<WindowsSmartaProvider>? _logger;
    private readonly ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)> _smartCache = new();
    private TimeSpan _cacheTtl;
    private long _cacheHits;
    private long _cacheMisses;

    public WindowsSmartaProvider(ILogger<WindowsSmartaProvider>? logger = null, IOptions<SmartaCacheOptions>? options = null)
    {
        _logger = logger;
        var minutes = options?.Value?.TtlMinutes ?? 10;
        _cacheTtl = TimeSpan.FromMinutes(Math.Max(1, minutes));
    }

    public Task SetCacheTtlMinutesAsync(int minutes, CancellationToken cancellationToken = default)
    {
        var m = Math.Max(1, minutes);
        _cacheTtl = TimeSpan.FromMinutes(m);
        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("SMART cache TTL set to {Minutes} minutes", m);
        }
        return Task.CompletedTask;
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
            // Try to return cached SMART data for this devicePath if available and not expired.
            var deviceKey = NormalizeDeviceKey(devicePath);
            if (_smartCache.TryGetValue(deviceKey, out var cached) && (DateTime.UtcNow - cached.Timestamp) < _cacheTtl)
            {
                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("GetSmartaDataAsync: returning cached SMART data for {DevicePath}", devicePath);
                }
                Interlocked.Increment(ref _cacheHits);
                var cachedCopy = cached.Data;
                cachedCopy.IsFromCache = true;
                cachedCopy.RetrievedAtUtc = cached.Timestamp;
                return cachedCopy;
            }

            if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("GetSmartaDataAsync: failed to obtain SMART data for {DevicePath} and no valid cache", devicePath);
            }
            Interlocked.Increment(ref _cacheMisses);
            return null;
        }

        var result = smartctl ?? windows;

        // If some important fields are missing try to supplement them from cache for the same device
        try
        {
            var deviceKey = NormalizeDeviceKey(devicePath);
            if (_smartCache.TryGetValue(deviceKey, out var cached))
            {
                var cachedData = cached.Data;
                if (result != null)
                {
                    if (string.IsNullOrEmpty(result.SerialNumber) || result.SerialNumber == "N/A")
                        result.SerialNumber = cachedData.SerialNumber;
                    if (string.IsNullOrEmpty(result.DeviceModel))
                        result.DeviceModel = cachedData.DeviceModel;
                    if (string.IsNullOrEmpty(result.FirmwareVersion))
                        result.FirmwareVersion = cachedData.FirmwareVersion;
                }
            }
        }
        catch { /* best-effort only */ }

        // Store result in cache under deviceKey and serial (if present)
        if (result != null)
        {
            var deviceKey = NormalizeDeviceKey(devicePath);
            result.IsFromCache = false;
            result.RetrievedAtUtc = DateTime.UtcNow;
            _smartCache[deviceKey] = (result, DateTime.UtcNow);

            if (!string.IsNullOrWhiteSpace(result.SerialNumber) && !string.Equals(result.SerialNumber, "N/A", StringComparison.OrdinalIgnoreCase))
            {
                var serialKey = NormalizeDeviceKey("SN:" + result.SerialNumber);
                _smartCache[serialKey] = (result, DateTime.UtcNow);
            }
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

    private static string NormalizeDeviceKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        return key.Trim().ToLowerInvariant();
    }

    // Cache management
    public Task ClearSmartCacheAsync(CancellationToken cancellationToken = default)
    {
        _smartCache.Clear();
        return Task.CompletedTask;
    }

    public Task RemoveSmartCacheForDeviceAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var key = NormalizeDeviceKey(devicePath);
        _smartCache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveSmartCacheForSerialAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return Task.CompletedTask;
        var key = NormalizeDeviceKey("SN:" + serialNumber);
        _smartCache.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<(int Hits, int Misses, int Items)> GetSmartCacheStatsAsync(CancellationToken cancellationToken = default)
    {
        var items = _smartCache.Count;
        var hits = (int)Interlocked.Read(ref _cacheHits);
        var misses = (int)Interlocked.Read(ref _cacheMisses);
        return Task.FromResult((hits, misses, items));
    }

    public async Task<List<SmartaAttributeItem>> GetSmartAttributesAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var execution = await ExecuteSmartctlCommandAsync(devicePath, "-j -a", cancellationToken);
        if (execution == null || string.IsNullOrWhiteSpace(execution.Value.Output))
        {
            if (_logger != null)
            {
                _logger.LogWarning("GetSmartAttributesAsync: No output from smartctl for {DevicePath}", devicePath);
            }
            return new List<SmartaAttributeItem>();
        }

        if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("GetSmartAttributesAsync: Received {Length} chars of JSON", execution.Value.Output.Length);
        }
        
        var attributes = SmartctlJsonParser.ParseAttributes(execution.Value.Output);
        
        if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("GetSmartAttributesAsync: Parsed {Count} attributes", attributes.Count);
        }
        
        if (attributes.Count == 0)
        {
            // Debug: Save JSON for analysis
            try
            {
                var debugPath = Path.Combine(Path.GetTempPath(), $"smart_debug_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.WriteAllText(debugPath, execution.Value.Output);
                if (_logger != null)
                {
                    _logger.LogWarning("GetSmartAttributesAsync: No attributes parsed. JSON saved to {DebugPath}", debugPath);
                }
            }
            catch { }
        }
        
        return attributes.ToList();
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

            // On Windows smartctl (Cygwin/MSYS builds), use /dev/pdN format
            // This format works correctly with smartmontools on Windows
            
            // Try Cygwin/MSYS format first (this is the one that WORKS!)
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

            // Use /dev/pdN format (Cygwin/MSYS) - this is the format that WORKS!
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