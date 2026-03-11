using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// SMART data provider for Windows systems using smartctl.
/// Based on LinuxSmartaProvider with Windows-specific paths.
/// </summary>
public class WindowsSmartaProvider : ISmartaProvider, IAdvancedSmartaProvider
{
    private readonly ILogger<WindowsSmartaProvider>? _logger;
    private readonly ConcurrentDictionary<string, (SmartaData Data, DateTime Timestamp)> _smartCache = new();
    private TimeSpan _cacheTtl = TimeSpan.FromMinutes(10);
    private long _cacheHits;
    private long _cacheMisses;

    // Static cache for smartctl path - shared across all instances
    private static string? s_cachedSmartctlPath;
    private static readonly object s_pathLock = new();

    // Windows smartctl paths (Cygwin/MSYS builds)
    private static readonly string[] SmartctlPaths = new[]
    {
        @"C:\Program Files\smartmontools\bin\smartctl.exe",
        @"C:\Program Files (x86)\smartmontools\bin\smartctl.exe",
        @"C:\ProgramData\chocolatey\bin\smartctl.exe",
        @"C:\tools\smartmontools\bin\smartctl.exe"
    };

    public WindowsSmartaProvider(ILogger<WindowsSmartaProvider>? logger = null)
    {
        _logger = logger;
    }

    public Task SetCacheTtlMinutesAsync(int minutes, CancellationToken cancellationToken = default)
    {
        _cacheTtl = TimeSpan.FromMinutes(Math.Max(1, minutes));
        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("SMART cache TTL set to {Minutes} minutes", minutes);
        }
        return Task.CompletedTask;
    }

    public async Task<SmartaData?> GetSmartaDataAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Getting SMART data for device: {DevicePath}", devicePath);
        }

        // Check cache first
        var cacheKey = NormalizeDeviceKey(devicePath);
        if (_smartCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.Timestamp) < _cacheTtl)
        {
            Interlocked.Increment(ref _cacheHits);
            cached.Data.IsFromCache = true;
            cached.Data.RetrievedAtUtc = cached.Timestamp;
            return cached.Data;
        }

        Interlocked.Increment(ref _cacheMisses);

        // Use smartctl to get SMART data
        var result = await ExecuteSmartctlForSmartDataAsync(devicePath, cancellationToken);
        
        if (result != null)
        {
            // Cache the result
            result.IsFromCache = false;
            result.RetrievedAtUtc = DateTime.UtcNow;
            _smartCache[cacheKey] = (result, DateTime.UtcNow);
        }

        return result;
    }

    public async Task<SmartaData?> GetAdvancedSmartaDataAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        return await GetSmartaDataAsync(devicePath, cancellationToken);
    }

    public async Task<List<SmartaAttributeItem>> GetSmartAttributesAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var execution = await ExecuteSmartctlCommandAsync(devicePath, "-j -a", cancellationToken);
        if (execution == null || string.IsNullOrWhiteSpace(execution.Value.Output))
        {
            if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("GetSmartAttributesAsync: No output from smartctl for {DevicePath}", devicePath);
            }
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

        var smartctlPath = FindSmartctlPath();
        if (string.IsNullOrEmpty(smartctlPath))
        {
            if (_logger != null && _logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError("smartctl not found. Cannot start self-test.");
            }
            Console.WriteLine("[WindowsSmartaProvider] ERROR: smartctl not found");
            return false;
        }

        // Detect device type and adjust arguments
        var deviceType = DetectDeviceType(devicePath);
        var devPath = ConvertToDevicePath(devicePath);
        var args = BuildSmartctlArgs($"-t {smartTestArgument}", devPath, deviceType);
        
        Console.WriteLine($"[WindowsSmartaProvider] StartSelfTest: {smartctlPath} {args}");
        
        var psi = new ProcessStartInfo
        {
            FileName = smartctlPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null) return false;
            
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            
            Console.WriteLine($"[WindowsSmartaProvider] StartSelfTest exit code: {process.ExitCode}");
            Console.WriteLine($"[WindowsSmartaProvider] Output: {output.Substring(0, Math.Min(200, output.Length))}");
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"[WindowsSmartaProvider] Error: {error.Substring(0, Math.Min(200, error.Length))}");
            }
            
            if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("smartctl self-test exit code: {ExitCode}, output: {Output}, error: {Error}", 
                    process.ExitCode, output, error);
            }
            
            // Exit codes: 0 = success, 4 = test already running or some other non-fatal condition
            if (process.ExitCode == 0 || process.ExitCode == 4)
            {
                return true;
            }
            
            // Check if the error indicates NVMe doesn't support the test
            if (error.Contains("Invalid Field in Command", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Invalid Field in Command", StringComparison.OrdinalIgnoreCase))
            {
                if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("NVMe drive does not support this self-test type: {TestType}", smartTestArgument);
                }
                // Try with explicit NVMe device type
                args = BuildSmartctlArgs($"-t {smartTestArgument}", devPath, "nvme");
                psi.Arguments = args;
                
                using var process2 = Process.Start(psi);
                if (process2 == null) return false;
                
                await process2.WaitForExitAsync(cancellationToken);
                return process2.ExitCode == 0 || process2.ExitCode == 4;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            if (_logger != null && _logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to start self-test for {DevicePath}", devicePath);
            }
            Console.WriteLine($"[WindowsSmartaProvider] EXCEPTION: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Convert Windows PhysicalDrive path to smartctl Cygwin/MSYS format
    /// \\.\PhysicalDrive0 -> /dev/sda  (smartctl uses /dev/sdX format in Windows)
    /// Note: smartctl --scan returns /dev/sda, /dev/sdb, etc. on Windows
    /// </summary>
    private static string ConvertToDevicePath(string windowsPath)
    {
        var driveNumber = new string(windowsPath.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(driveNumber))
        {
            driveNumber = "0";
        }
        
        // Convert PhysicalDrive number to sdX letter (0=a, 1=b, 2=c, etc.)
        var index = int.Parse(driveNumber);
        var sdLetter = (char)('a' + index);
        return $"/dev/sd{sdLetter}";
    }
    
    private static string DetectDeviceType(string devicePath)
    {
        // On Windows, we need to query the device type via smartctl
        // For now, default to "auto" and let smartctl figure it out
        // NVMe detection is done in BuildSmartctlArgs if needed
        if (devicePath.Contains("nvme", StringComparison.OrdinalIgnoreCase))
            return "nvme";
        return "auto";
    }
    
    private static string BuildSmartctlArgs(string baseArgs, string devicePath, string deviceType)
    {
        // For NVMe devices, we need to specify the device type explicitly
        if (deviceType == "nvme")
        {
            return $"-d nvme {baseArgs} {devicePath}";
        }
        return $"{baseArgs} {devicePath}";
    }

    public async Task<SmartaSelfTestStatus> GetSelfTestStatusAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[WindowsSmartaProvider] GetSelfTestStatusAsync for: {devicePath}");
        
        var execution = await ExecuteSmartctlCommandAsync(devicePath, "-j -a", cancellationToken);
        if (execution == null || string.IsNullOrWhiteSpace(execution.Value.Output))
        {
            Console.WriteLine("[WindowsSmartaProvider] GetSelfTestStatusAsync: No output from smartctl");
            return SmartaSelfTestStatus.Unknown;
        }

        var result = SmartctlJsonParser.Parse(execution.Value.Output);
        var status = result?.CurrentSelfTest?.Status ?? SmartaSelfTestStatus.Unknown;
        Console.WriteLine($"[WindowsSmartaProvider] GetSelfTestStatusAsync: Parsed status = {status}");
        Console.WriteLine($"[WindowsSmartaProvider] CurrentSelfTest = {result?.CurrentSelfTest?.Status}, SelfTests count = {result?.SelfTests?.Count ?? 0}");
        return status;
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
        return Task.FromResult(new List<SmartaMaintenanceAction>());
    }

    public Task<bool> ExecuteMaintenanceActionAsync(string devicePath, SmartaMaintenanceAction action, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

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
        return Task.FromResult((Interlocked.Read(ref _cacheHits).CompareTo(0) >= 0 ? (int)Interlocked.Read(ref _cacheHits) : 0,
            Interlocked.Read(ref _cacheMisses).CompareTo(0) >= 0 ? (int)Interlocked.Read(ref _cacheMisses) : 0,
            _smartCache.Count));
    }

    public async Task<bool> IsDriveValidAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var drives = await ListDrivesAsync(cancellationToken);
        return drives.Any(d => d.Equals(devicePath, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<string>> ListDrivesAsync(CancellationToken cancellationToken = default)
    {
        var drives = new List<string>();

        var smartctlPath = await FindSmartctlPathAsync();
        
        if (!string.IsNullOrEmpty(smartctlPath))
        {
            try
            {
                // Use smartctl --scan for Windows
                var psi = new ProcessStartInfo
                {
                    FileName = smartctlPath,
                    Arguments = "--scan",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return drives;

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                // Parse output: /dev/pd0 -d ata # /dev/pd0, ATA device
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    // Extract device path
                    var parts = trimmed.Split(' ', '\t');
                    if (parts.Length > 0 && parts[0].StartsWith("/dev/", StringComparison.Ordinal))
                    {
                        drives.Add(parts[0]);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_logger != null && _logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(ex, "Failed to list drives via smartctl --scan");
                }
            }
        }

        // Fallback: Use WMI via PowerShell
        if (drives.Count == 0)
        {
            try
            {
                var tempScriptPath = Path.Combine(Path.GetTempPath(), $"list_drives_{Guid.NewGuid():N}.ps1");
                var script = @"Get-CimInstance Win32_DiskDrive | ForEach-Object { $_.DeviceID }";
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
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                    await process.WaitForExitAsync(cancellationToken);
                    
                    foreach (var line in output.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            drives.Add(trimmed);
                        }
                    }
                }
                
                try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
            }
            catch (Exception innerEx)
            {
                if (_logger != null && _logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(innerEx, "Failed to list drives via WMI");
                }
            }
        }

        Console.WriteLine($"[WindowsSmartaProvider] ListDrivesAsync: Found {drives.Count} drives");
        return drives;
    }

    public async Task<string> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default)
    {
        if (FindSmartctlPath() != null) return string.Empty;
        return "Nainstalujte: winget install smartmontools";
    }

    public Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default)
    {
        // Cannot reliably auto-install on Windows without admin rights
        // Just return true if smartctl is already available
        return Task.FromResult(IsSmartctlAvailable());
    }

    public async Task<int?> GetTemperatureOnlyAsync(string devicePath, CancellationToken cancellationToken = default)
    {
        var data = await GetSmartaDataAsync(devicePath, cancellationToken);
        return data?.Temperature > 0 ? data.Temperature : null;
    }

    private async Task<SmartaData?> ExecuteSmartctlForSmartDataAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            var execution = await ExecuteSmartctlCommandAsync(devicePath, "-j -a", cancellationToken);
            
            Console.WriteLine($"[WindowsSmartaProvider] ExecuteSmartctlForSmartDataAsync: devicePath={devicePath}");
            
            if (execution == null)
            {
                Console.WriteLine("[WindowsSmartaProvider] ERROR: execution is null (smartctl not found or crash)");
                return null;
            }
            
            Console.WriteLine($"[WindowsSmartaProvider] ExitCode={execution.Value.ExitCode}, Output length={execution.Value.Output?.Length ?? 0}");
            
            if (execution.Value.ExitCode != 0 && execution.Value.ExitCode != 1)
            {
                if (execution.Value.ExitCode >= 2)
                {
                    Console.WriteLine($"[WindowsSmartaProvider] WARNING: smartctl exit code {execution.Value.ExitCode}, Error: {execution.Value.Error}");
                }
            }
            
            if (string.IsNullOrWhiteSpace(execution.Value.Output))
            {
                Console.WriteLine("[WindowsSmartaProvider] ERROR: Output is empty");
                return null;
            }

            var result = SmartctlJsonParser.Parse(execution.Value.Output);
            if (result == null)
            {
                Console.WriteLine("[WindowsSmartaProvider] ERROR: Parser returned null");
                return null;
            }
            
            Console.WriteLine($"[WindowsSmartaProvider] Parsed OK: Model={result.DeviceModel}, SelfTests={result.SelfTests?.Count ?? 0}");
            
            return SmartctlJsonParser.ToSmartaData(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WindowsSmartaProvider] EXCEPTION: {ex.Message}");
            if (_logger != null && _logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to get SMART data for {DevicePath}", devicePath);
            }
            return null;
        }
    }

    private async Task<(int ExitCode, string Output, string Error)?> ExecuteSmartctlCommandAsync(string devicePath, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            // Find smartctl path
            var smartctlPath = FindSmartctlPath();
            if (string.IsNullOrEmpty(smartctlPath))
            {
                Console.WriteLine("[WindowsSmartaProvider] ERROR: smartctl path not found!");
                if (_logger != null && _logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError("smartctl is not available. Please install smartmontools.");
                }
                return null;
            }

            // Convert Windows path to Cygwin/MSYS format and detect device type
            var devPath = ConvertToDevicePath(devicePath);
            var deviceType = DetectDeviceType(devicePath);
            var args = BuildSmartctlArgsForQuery(arguments, devPath, deviceType);
            
            Console.WriteLine($"[WindowsSmartaProvider] Running: {smartctlPath} {args}");
            
            var psi = new ProcessStartInfo
            {
                FileName = smartctlPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Console.WriteLine("[WindowsSmartaProvider] ERROR: Failed to start smartctl process");
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var outputPreview = output.Length > 200 ? string.Concat(output.AsSpan(0, 200), "...") : output;
            Console.WriteLine($"[WindowsSmartaProvider] Exit code: {process.ExitCode}, Output: {outputPreview}");
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"[WindowsSmartaProvider] Error: {error.Substring(0, Math.Min(200, error.Length))}");
            }

            if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("smartctl {Args} exit code: {ExitCode}", args, process.ExitCode);
            }
            
            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WindowsSmartaProvider] EXCEPTION in ExecuteSmartctlCommandAsync: {ex.Message}");
            if (_logger != null && _logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(ex, "Failed to execute smartctl for {DevicePath}", devicePath);
            }
            return null;
        }
    }
    
    private static string BuildSmartctlArgsForQuery(string baseArgs, string devicePath, string deviceType)
    {
        // For NVMe devices, we need to specify the device type explicitly
        if (deviceType == "nvme")
        {
            return $"-d nvme {baseArgs} {devicePath}";
        }
        return $"{baseArgs} {devicePath}";
    }

    private static string? FindSmartctlPath()
    {
        // Check static cache first
        lock (s_pathLock)
        {
            if (s_cachedSmartctlPath != null && File.Exists(s_cachedSmartctlPath))
                return s_cachedSmartctlPath;
        }

        // Check known paths
        foreach (var path in SmartctlPaths)
        {
            if (File.Exists(path))
            {
                lock (s_pathLock)
                {
                    s_cachedSmartctlPath = path;
                }
                return path;
            }
        }

        return null;
    }

    private static async Task<string?> FindSmartctlPathAsync()
    {
        // Check static cache first
        lock (s_pathLock)
        {
            if (s_cachedSmartctlPath != null && File.Exists(s_cachedSmartctlPath))
                return s_cachedSmartctlPath;
        }

        // Check known paths synchronously first (faster)
        foreach (var path in SmartctlPaths)
        {
            if (File.Exists(path))
            {
                lock (s_pathLock)
                {
                    s_cachedSmartctlPath = path;
                }
                return path;
            }
        }

        // Try 'where' command as fallback
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
            if (process == null) return null;
            
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken: default);
            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var path = output.Trim().Split('\n')[0].Trim();
                if (File.Exists(path))
                {
                    lock (s_pathLock)
                    {
                        s_cachedSmartctlPath = path;
                    }
                    return path;
                }
            }
        }
        catch
        {
            // Ignore errors from 'where'
        }

        return null;
    }

    private static bool IsSmartctlAvailable()
    {
        return FindSmartctlPath() != null;
    }

    private static string NormalizeDeviceKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        return key.Trim().ToLowerInvariant();
    }
}