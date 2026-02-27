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
    /// Cache of last successful SMART data reads to use as fallback when current read fails.
    /// This prevents showing N/A for serial number and temperature during active disk access.
    /// </summary>
    private SmartaData? _lastSuccessfulSmartData;

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
        // On Windows, smartctl is more reliable for SMART data and serial number
        // Get data from both sources
        var smartctlTask = GetSmartaDataViaSmartctlAsync(drivePath, cancellationToken);
        var windowsTask = GetSmartaDataViaPowerShellAsync(drivePath, cancellationToken);

        await Task.WhenAll(smartctlTask, windowsTask);

        var smartctl = await smartctlTask;
        var windows = await windowsTask;

        if (smartctl == null && windows == null) 
        {
            // Both failed - use last successful data if available (for live updates during disk access)
            return _lastSuccessfulSmartData;
        }

        // Prefer smartctl data - it has correct serial number and SMART metrics
        // If smartctl fails (e.g., during disk access), use Windows data
        var result = smartctl ?? windows;

        // If we have both, smartctl takes priority for everything
        if (smartctl != null && windows != null)
        {
            // Use Windows model if smartctl model is missing or contains "USB Device"
            if ((string.IsNullOrEmpty(smartctl.DeviceModel) || smartctl.DeviceModel.Contains("USB", StringComparison.OrdinalIgnoreCase)) 
                && !string.IsNullOrEmpty(windows.DeviceModel))
            {
                result!.DeviceModel = windows.DeviceModel;
            }
        }

        // Merge with cached data for static fields
        // Serial number, model, firmware don't change - use cached values if current is missing
        if (result != null && _lastSuccessfulSmartData != null)
        {
            // Serial number is static - never changes, always use cached if available
            if (string.IsNullOrEmpty(result.SerialNumber) || result.SerialNumber == "N/A")
            {
                result.SerialNumber = _lastSuccessfulSmartData.SerialNumber;
            }

            // Model and firmware are also static
            if (string.IsNullOrEmpty(result.DeviceModel))
            {
                result.DeviceModel = _lastSuccessfulSmartData.DeviceModel;
            }
            if (string.IsNullOrEmpty(result.FirmwareVersion))
            {
                result.FirmwareVersion = _lastSuccessfulSmartData.FirmwareVersion;
            }
            if (string.IsNullOrEmpty(result.ModelFamily))
            {
                result.ModelFamily = _lastSuccessfulSmartData.ModelFamily;
            }
        }

        // Only cache if we have real data (not just fallback)
        if (result != null && !string.IsNullOrEmpty(result.SerialNumber) && result.SerialNumber != "N/A")
        {
            _lastSuccessfulSmartData = result;
        }

        return result;
    }

    /// <summary>
    /// Detects if the serial number looks like it's from a USB controller rather than the actual drive.
    /// </summary>
    private static bool IsUsbControllerSerialNumber(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
            return false;

        var upperSN = serialNumber.ToUpperInvariant();
        
        // Known USB controller manufacturer patterns - these are DEFINITIVE
        var knownUsbControllers = new[] 
        {
            "PROLIFIC",     // Prolific USB controller (e.g., PL2303, PL2309)
            "JMICRON",      // JMicron USB controller
            "VIA",          // VIA USB controller
            "SILICON",      // Silicon Image USB controller
            "REALTEK",      // Realtek USB controller
            "ASMEDIA",      // ASMedia USB controller
            "INITIO",       // Initio USB controller
            "CYPRESS",      // Cypress USB controller
        };

        // Check if it starts with known USB controller patterns
        if (knownUsbControllers.Any(p => upperSN.StartsWith(p, StringComparison.Ordinal)))
            return true;

        // Pattern: all zeros or all F's with manufacturer prefix (e.g., "000000002", "FFFFFFFF")
        // This indicates USB bridge controller, not drive
        if ((upperSN.All(c => c == '0') || upperSN.All(c => c == 'F')) && upperSN.Length <= 16)
            return true;

        return false;
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

            // PowerShell script - only gets basic info as fallback
            // Smartctl is preferred for serial number and SMART data
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
        $res.FirmwareVersion = $drive.FirmwareRevision
    }}
}} catch {{}}

# Output as JSON
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
        // First, try to detect the correct device type using smartctl --scan-open
        var detectedDeviceType = await DetectSmartctlDeviceTypeAsync(drivePath, cancellationToken);
        
        // Try with detected device type first
        if (!string.IsNullOrEmpty(detectedDeviceType))
        {
            var data = await ExecuteSmartctlAsync(drivePath, detectedDeviceType, cancellationToken);
            if (!IsDataEmpty(data))
            {
                return data;
            }
        }
        
        // Fallback: Try common device types
        var deviceTypes = new[] { "auto", "sat", "scsi", "usbprolific", "usbsunplus", "usbjmicron" };
        var results = new List<(string? deviceType, SmartaData? data, int score)>();
        
        foreach (var deviceType in deviceTypes)
        {
            var data = await ExecuteSmartctlAsync(drivePath, deviceType, cancellationToken);
            if (data != null)
            {
                int score = ScoreSmartData(data);
                results.Add((deviceType, data, score));
            }
        }
        
        // Return the result with highest score
        if (results.Count > 0)
        {
            var best = results.OrderByDescending(r => r.score).First();
            return best.data;
        }
        
        return null;
    }

    /// <summary>
    /// Detects the correct smartctl device type using --scan-open.
    /// </summary>
    private async Task<string?> DetectSmartctlDeviceTypeAsync(string drivePath, CancellationToken cancellationToken)
    {
        try
        {
            var path = await FindSmartctlPathAsync();
            if (string.IsNullOrEmpty(path)) return null;

            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--scan-open",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            // Parse output to find device type for this drive
            // Output format: /dev/sdc -d usbprolific # /dev/sdc [USB Prolific], ATA device
            var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            
            // Extract drive number from path
            var driveNumber = new string(drivePath.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(driveNumber)) return null;

            // Look for the line with this drive number (smartctl maps to /dev/sdX)
            // PhysicalDrive2 = /dev/sdc (a=0, b=1, c=2)
            var devName = $"sd{(char)('a' + int.Parse(driveNumber))}";
            
            foreach (var line in lines)
            {
                // Look for lines mentioning this device
                if (line.Contains(devName) && !line.StartsWith('#'))
                {
                    // Extract device type: -d usbprolific
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"-d\s+(\S+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Scores SMART data completeness. Higher score = better/more complete data.
    /// </summary>
    private int ScoreSmartData(SmartaData data)
    {
        int score = 0;
        
        // Temperature is most important (indicates real SMART data, not just metadata)
        if (data.Temperature > 0) score += 100;
        
        // Power-on hours is also critical for drive health assessment
        if (data.PowerOnHours > 0) score += 80;
        
        // Model information
        if (!string.IsNullOrEmpty(data.DeviceModel)) score += 40;
        
        // Serial number (prefer real SN over USB controller ID)
        if (!string.IsNullOrEmpty(data.SerialNumber) && !IsUsbControllerSerialNumber(data.SerialNumber))
            score += 30;
        
        // Additional SMART attributes
        if (data.ReallocatedSectorCount > 0) score += 20;
        if (data.PendingSectorCount > 0) score += 20;
        if (data.UncorrectableErrorCount > 0) score += 20;
        if (data.WearLevelingCount.HasValue) score += 15;
        if (!string.IsNullOrEmpty(data.FirmwareVersion)) score += 10;
        if (!string.IsNullOrEmpty(data.ModelFamily)) score += 10;
        
        return score;
    }

    private bool IsDataEmpty(SmartaData? d) => d == null || (d.Temperature <= 0 && d.PowerOnHours <= 0 && string.IsNullOrEmpty(d.DeviceModel));

    private async Task<SmartaData?> ExecuteSmartctlAsync(string drivePath, string? deviceType, CancellationToken cancellationToken)
    {
        try
        {
            var path = await FindSmartctlPathAsync();
            if (string.IsNullOrEmpty(path)) return null;

            // Convert Windows physical drive path to /dev/sdX format
            // Extract drive number from path like \\.\PhysicalDrive2
            var driveNumber = new string(drivePath.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(driveNumber))
            {
                return null;
            }

            // Convert to /dev/sdX format (sda=0, sdb=1, sdc=2, etc)
            var devPath = $"/dev/sd{(char)('a' + int.Parse(driveNumber))}";

            // Build arguments
            string args;
            if (!string.IsNullOrEmpty(deviceType))
            {
                args = $"-T permissive -j -d {deviceType} -a {devPath}";
            }
            else
            {
                args = $"-T permissive -j -a {devPath}";
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
if (Get-Command Get-Disk -ErrorAction SilentlyContinue) {
    Get-Disk | ForEach-Object {
        $result += [PSCustomObject]@{
            DeviceID = ""\\\\.\\PhysicalDrive$($_.Number)""
            Model = $_.FriendlyName
            Size = $_.Size
        }
    }
}
if ($result.Count -eq 0) {
    Get-CimInstance Win32_DiskDrive | ForEach-Object {
        $result += [PSCustomObject]@{
            DeviceID = $_.DeviceID
            Model = $_.Model
            Size = $_.Size
        }
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

    /// <summary>
    /// Gets ONLY the temperature from Windows (fast, works even when disk is locked).
    /// This uses WMI which is more reliable during disk operations.
    /// </summary>
    public async Task<int?> GetTemperatureOnlyAsync(string drivePath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract disk number from path (e.g., "\\.\PHYSICALDRIVE2" -> "2")
            var diskNumber = ExtractDiskNumber(drivePath);
            if (diskNumber == null)
            {
                return null;
            }

            // PowerShell script to get JUST temperature from WMI
            string script = $@"
try {{
    $diskNumber = {diskNumber}
    
    # Try to get temperature from MSStorageDriver_ATAPISmartData
    $smartData = Get-WmiObject -Namespace 'root\wmi' -Class MSStorageDriver_ATAPISmartData -ErrorAction SilentlyContinue | 
        Where-Object {{ $_.InstanceName -like ""*PhysicalDrive$diskNumber*"" }}
    
    if ($smartData) {{
        $vendorSpecific = $smartData.VendorSpecific
        if ($vendorSpecific -and $vendorSpecific.Length -ge 362) {{
            # Temperature is usually at offset 190-191 (ID 194)
            # Format: [ID][Current][Worst][Reserved][Data1][Data2][Data3][Data4]
            # We look for attribute ID 194 (0xC2 = 194)
            for ($i = 2; $i -lt 362; $i += 12) {{
                $id = $vendorSpecific[$i]
                if ($id -eq 194) {{  # Temperature attribute
                    $current = $vendorSpecific[$i + 5]  # Current temp is in raw value
                    if ($current -gt 0 -and $current -lt 100) {{
                        Write-Output $current
                        exit 0
                    }}
                }}
            }}
        }}
    }}
    
    # Fallback: Try Get-PhysicalDisk
    $disk = Get-PhysicalDisk -DeviceNumber $diskNumber -ErrorAction SilentlyContinue
    if ($disk -and $disk.OperationalStatus -eq 'OK') {{
        # Some disks report health sensor temperature
        $temp = Get-CimInstance -Namespace 'root\wmi' -ClassName MSStorageDriver_FailurePredictData -ErrorAction SilentlyContinue |
            Where-Object {{ $_.InstanceName -like ""*PhysicalDrive$diskNumber*"" }}
        
        if ($temp) {{
            Write-Output $temp.VendorSpecific[0]
        }}
    }}
}} catch {{
    # Silent fail
}}
exit 1
";

            string tempScriptPath = Path.Combine(Path.GetTempPath(), $"temp_check_{Guid.NewGuid():N}.ps1");
            
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
                
                if (!string.IsNullOrWhiteSpace(output) && int.TryParse(output.Trim(), out int temp))
                {
                    return temp;
                }
                
                return null;
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

    private static int? ExtractDiskNumber(string drivePath)
    {
        if (string.IsNullOrEmpty(drivePath))
            return null;

        // Extract number from "\\.\PHYSICALDRIVE2" or similar
        var digits = new string(drivePath.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out int number))
        {
            return number;
        }

        return null;
    }
}
