using System.Diagnostics;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

public class WindowsSmartaProvider : ISmartaProvider
{
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
        var result = new SmartaData();

        // Priority for identification: Windows > smartctl
        result.DeviceModel = PickNotEmpty(windows?.DeviceModel, smartctl?.DeviceModel) ?? "Neznámý model";
        result.SerialNumber = PickNotEmpty(windows?.SerialNumber, smartctl?.SerialNumber) ?? "Neznámé S/N";
        result.FirmwareVersion = PickNotEmpty(windows?.FirmwareVersion, smartctl?.FirmwareVersion) ?? "---";
        
        // Priority for stats: Max non-zero value
        result.Temperature = Math.Max(windows?.Temperature ?? 0, smartctl?.Temperature ?? 0);
        result.PowerOnHours = Math.Max(windows?.PowerOnHours ?? 0, smartctl?.PowerOnHours ?? 0);
        result.ReallocatedSectorCount = Math.Max(windows?.ReallocatedSectorCount ?? 0, smartctl?.ReallocatedSectorCount ?? 0);
        result.PendingSectorCount = Math.Max(windows?.PendingSectorCount ?? 0, smartctl?.PendingSectorCount ?? 0);
        result.UncorrectableErrorCount = Math.Max(windows?.UncorrectableErrorCount ?? 0, smartctl?.UncorrectableErrorCount ?? 0);
        
        if (smartctl?.WearLevelingCount > 0) result.WearLevelingCount = smartctl.WearLevelingCount;
        else if (windows?.WearLevelingCount > 0) result.WearLevelingCount = windows.WearLevelingCount;

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
            // Escape drivePath for PS: \\.\PhysicalDrive0 -> \\\\.\\PhysicalDrive0
            var escapedPath = drivePath.Replace("\\", "\\\\");
            var powershellScript = $@"
$ErrorActionPreference = 'SilentlyContinue'
$path = '{escapedPath}'
$cim = Get-CimInstance Win32_DiskDrive | Where-Object {{ $_.DeviceID -eq $path }}
$pd = Get-PhysicalDisk | Where-Object {{ ""\\\\.\\PhysicalDrive$($_.DeviceId)"" -eq $path -or $_.DeviceID -eq $path }}

$res = @{{
    Model = if ($cim) {{ $cim.Model }} else {{ '' }}
    SerialNumber = if ($cim) {{ $cim.SerialNumber }} else {{ '' }}
    FirmwareVersion = if ($cim) {{ $cim.FirmwareRevision }} else {{ '' }}
    Temperature = 0
    PowerOnHours = 0
}}

if ($pd) {{
    $report = Get-StorageHealthReport -EntityPhysicalDisk $pd.UniqueId
    if (-not $report) {{ $report = Get-StorageHealthReport -EntityPhysicalDisk $pd.ObjectId }}
    if ($report) {{
        foreach ($attr in $report.Attributes) {{
            if ($attr.Name -like '*Temperature*') {{ $res.Temperature = $attr.Value }}
            if ($attr.Name -like '*PowerOn*') {{ $res.PowerOnHours = $attr.Value }}
            if ($attr.Name -like '*Reallocated*') {{ $res.ReallocatedSectorCount = $attr.Value }}
        }}
    }}
    $rel = Get-StorageReliabilityCounter -PhysicalDisk $pd
    if ($rel) {{
        if ($res.Temperature -le 0) {{ $res.Temperature = $rel.Temperature }}
        if ($res.PowerOnHours -le 0) {{ $res.PowerOnHours = $rel.LoadHours }}
        $res.UncorrectableErrorCount = $rel.ReadErrorsTotal
        if ($rel.Wear -gt 0) {{ $res.WearLevelingCount = 100 - $rel.Wear }}
    }}
}}
$res | ConvertTo-Json -Compress";

            var psi = new ProcessStartInfo { FileName = "powershell", Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{powershellScript}\"", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(output) ? null : WindowsSmartJsonParser.Parse(output, "[]");
        }
        catch { return null; }
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
            if (!string.IsNullOrEmpty(deviceType)) args = $"-d {deviceType} {args}";
            var psi = new ProcessStartInfo { FileName = path, Arguments = args, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(psi);
            if (process == null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(output) ? null : SmartctlJsonParser.Parse(output);
        }
        catch { return null; }
    }

    private async Task<string?> FindSmartctlPathAsync()
    {
        if (await IsSmartctlInPathAsync()) return "smartctl";
        var common = new[] { @"C:\Program Files\smartmontools\bin\smartctl.exe", @"C:\Program Files (x86)\smartmontools\bin\smartctl.exe" };
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
        var ps = "Get-CimInstance Win32_DiskDrive | Select-Object DeviceID, Model, Size, SerialNumber | ConvertTo-Csv -NoTypeInformation";
        var psi = new ProcessStartInfo { FileName = "powershell", Arguments = $"-NoProfile -Command \"{ps}\"", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(psi);
        if (process == null) return drives;
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        foreach (var line in output.Split('\n').Skip(1))
        {
            var p = line.Split(',').Select(x => x.Trim('\"', ' ', '\r')).ToArray();
            if (p.Length >= 3 && long.TryParse(p[2], out var s)) drives.Add(new CoreDriveInfo { Path = p[0], Name = p[1], TotalSize = s });
        }
        return drives;
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
            var psi = new ProcessStartInfo { FileName = "winget", Arguments = "install --id smartmontools.smartmontools --silent --accept-package-agreements --accept-source-agreements", UseShellExecute = false, CreateNoWindow = false };
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
            var psi = new ProcessStartInfo { FileName = "where", Arguments = "smartctl", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private int GetDriveNumber(string drivePath) => int.TryParse(new string(drivePath.Where(char.IsDigit).ToArray()), out var n) ? n : 0;
}
