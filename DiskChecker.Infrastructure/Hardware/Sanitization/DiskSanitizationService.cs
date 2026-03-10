using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace DiskChecker.Infrastructure.Hardware.Sanitization;

/// <summary>
/// Result of disk sanitization operation.
/// </summary>
public class SanitizationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long BytesWritten { get; set; }
    public long BytesRead { get; set; }
    public double WriteSpeedMBps { get; set; }
    public double ReadSpeedMBps { get; set; }
    public int ErrorsDetected { get; set; }
    public TimeSpan Duration { get; set; }
    public bool PartitionCreated { get; set; }
    public bool Formatted { get; set; }
}

/// <summary>
/// Progress callback for sanitization operations.
/// </summary>
public class SanitizationProgress
{
    public string Phase { get; set; } = "";
    public double ProgressPercent { get; set; }
    public long BytesProcessed { get; set; }
    public long TotalBytes { get; set; }
    public double CurrentSpeedMBps { get; set; }
    public int Errors { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
}

/// <summary>
/// Service for performing destructive disk sanitization.
/// This will ERASE ALL DATA on the disk!
/// </summary>
public class DiskSanitizationService
{
    private readonly ILogger<DiskSanitizationService>? _logger;
    private const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x70140;
    private const int BUFFER_SIZE = 64 * 1024 * 1024; // 64 MB buffer

    public DiskSanitizationService(ILogger<DiskSanitizationService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Perform full disk sanitization: write zeros, read/verify, partition, format.
    /// WARNING: This destroys ALL data on the disk!
    /// </summary>
    public async Task<SanitizationResult> SanitizeDiskAsync(
        string devicePath,
        long diskSize,
        bool createPartition = true,
        bool formatNtfs = true,
        string volumeLabel = "SCCM",
        IProgress<SanitizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new SanitizationResult();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Phase 1: Write zeros
            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Starting disk sanitization for {DevicePath}", devicePath);
            }
            
            progress?.Report(new SanitizationProgress
            {
                Phase = "Zápis nul",
                ProgressPercent = 0,
                TotalBytes = diskSize
            });

            var writeResult = await WriteZerosAsync(devicePath, diskSize, progress, cancellationToken);
            if (!writeResult.Success)
            {
                result.ErrorMessage = writeResult.ErrorMessage;
                return result;
            }
            
            result.BytesWritten = writeResult.BytesWritten;
            result.WriteSpeedMBps = writeResult.SpeedMBps;
            result.ErrorsDetected += writeResult.Errors;

            // Phase 2: Read and verify
            progress?.Report(new SanitizationProgress
            {
                Phase = "Čtení a ověření",
                ProgressPercent = 0,
                TotalBytes = diskSize
            });

            var readResult = await ReadAndVerifyAsync(devicePath, diskSize, progress, cancellationToken);
            if (!readResult.Success)
            {
                result.ErrorMessage = readResult.ErrorMessage;
                return result;
            }

            result.BytesRead = readResult.BytesRead;
            result.ReadSpeedMBps = readResult.SpeedMBps;
            result.ErrorsDetected += readResult.Errors;

            // Phase 3: Create GPT partition
            if (createPartition)
            {
                progress?.Report(new SanitizationProgress
                {
                    Phase = "Vytváření GPT oddílu",
                    ProgressPercent = 0
                });

                var partitionResult = await CreateGptPartitionAsync(devicePath, cancellationToken);
                if (!partitionResult.Success)
                {
                    result.ErrorMessage = partitionResult.ErrorMessage;
                    return result;
                }
                result.PartitionCreated = true;
            }

            // Phase 4: Format NTFS
            if (formatNtfs && createPartition)
            {
                progress?.Report(new SanitizationProgress
                {
                    Phase = "Formátování NTFS",
                    ProgressPercent = 0
                });

                var formatResult = await FormatNtfsAsync(devicePath, volumeLabel, cancellationToken);
                if (!formatResult.Success)
                {
                    result.ErrorMessage = formatResult.ErrorMessage;
                    return result;
                }
                result.Formatted = true;
            }

            result.Success = true;
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Disk sanitization completed successfully for {DevicePath}. Duration: {Duration}", 
                    devicePath, result.Duration);
            }
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Operace zrušena uživatelem";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during disk sanitization for {DevicePath}", devicePath);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<SanitizationPhaseResult> WriteZerosAsync(
        string devicePath,
        long diskSize,
        IProgress<SanitizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new SanitizationPhaseResult();
        var stopwatch = Stopwatch.StartNew();
        var buffer = new byte[BUFFER_SIZE];
        long bytesWritten = 0;

        try
        {
            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            var physicalPath = $@"\\.\PhysicalDrive{driveNumber}";

            using var handle = CreateFile(physicalPath, Win32DiskInterop.GENERIC_READ | Win32DiskInterop.GENERIC_WRITE);
            if (handle.IsInvalid)
            {
                result.ErrorMessage = $"Nelze otevřít disk: {physicalPath}";
                return result;
            }

            // Write zeros in chunks
            while (bytesWritten < diskSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytesToWrite = (int)Math.Min(BUFFER_SIZE, diskSize - bytesWritten);
                var bytesWrittenThisChunk = await WriteFileAsync(handle, buffer, bytesToWrite, cancellationToken);
                
                if (bytesWrittenThisChunk != bytesToWrite)
                {
                    result.Errors++;
                    if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("Write incomplete at offset {Offset}: wrote {Written} of {Expected}", 
                            bytesWritten, bytesWrittenThisChunk, bytesToWrite);
                    }
                }

                bytesWritten += bytesWrittenThisChunk;

                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var speed = elapsed > 0 ? bytesWritten / (1024.0 * 1024.0) / elapsed : 0;
                var remaining = diskSize - bytesWritten;
                var eta = speed > 0 ? TimeSpan.FromSeconds(remaining / (1024.0 * 1024.0) / speed) : (TimeSpan?)null;

                progress?.Report(new SanitizationProgress
                {
                    Phase = "Zápis nul",
                    ProgressPercent = (double)bytesWritten / diskSize * 100,
                    BytesProcessed = bytesWritten,
                    TotalBytes = diskSize,
                    CurrentSpeedMBps = speed,
                    Errors = result.Errors,
                    EstimatedTimeRemaining = eta
                });
            }

            result.Success = true;
            result.BytesWritten = bytesWritten;
            result.SpeedMBps = stopwatch.Elapsed.TotalSeconds > 0 
                ? bytesWritten / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds 
                : 0;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<SanitizationPhaseResult> ReadAndVerifyAsync(
        string devicePath,
        long diskSize,
        IProgress<SanitizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new SanitizationPhaseResult();
        var stopwatch = Stopwatch.StartNew();
        var buffer = new byte[BUFFER_SIZE];
        long bytesRead = 0;

        try
        {
            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            var physicalPath = $@"\\.\PhysicalDrive{driveNumber}";

            using var handle = CreateFile(physicalPath, Win32DiskInterop.GENERIC_READ);
            if (handle.IsInvalid)
            {
                result.ErrorMessage = $"Nelze otevřít disk: {physicalPath}";
                return result;
            }

            // Read and verify all zeros
            while (bytesRead < diskSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytesToRead = (int)Math.Min(BUFFER_SIZE, diskSize - bytesRead);
                var bytesReadThisChunk = await ReadFileAsync(handle, buffer, bytesToRead, cancellationToken);

                if (bytesReadThisChunk != bytesToRead)
                {
                    result.Errors++;
                    if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("Read incomplete at offset {Offset}: read {Read} of {Expected}", 
                            bytesRead, bytesReadThisChunk, bytesToRead);
                    }
                }

                // Verify all zeros
                foreach (var b in buffer.AsSpan(0, bytesReadThisChunk))
                {
                    if (b != 0)
                    {
                        result.Errors++;
                    }
                }

                bytesRead += bytesReadThisChunk;

                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var speed = elapsed > 0 ? bytesRead / (1024.0 * 1024.0) / elapsed : 0;
                var remaining = diskSize - bytesRead;
                var eta = speed > 0 ? TimeSpan.FromSeconds(remaining / (1024.0 * 1024.0) / speed) : (TimeSpan?)null;

                progress?.Report(new SanitizationProgress
                {
                    Phase = "Čtení a ověření",
                    ProgressPercent = (double)bytesRead / diskSize * 100,
                    BytesProcessed = bytesRead,
                    TotalBytes = diskSize,
                    CurrentSpeedMBps = speed,
                    Errors = result.Errors,
                    EstimatedTimeRemaining = eta
                });
            }

            result.Success = true;
            result.BytesRead = bytesRead;
            result.SpeedMBps = stopwatch.Elapsed.TotalSeconds > 0 
                ? bytesRead / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds 
                : 0;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<SanitizationPhaseResult> CreateGptPartitionAsync(
        string devicePath,
        CancellationToken cancellationToken)
    {
        var result = new SanitizationPhaseResult();

        try
        {
            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            
            // Use diskpart to create GPT partition
            var diskpartScript = $@"select disk {driveNumber}
clean
convert gpt
create partition primary
assign";

            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"diskpart_{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(tempScriptPath, diskpartScript, cancellationToken);

                var psi = new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = $"/s \"{tempScriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas" // Run as administrator
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    result.ErrorMessage = "Nelze spustit diskpart";
                    return result;
                }

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    result.ErrorMessage = $"diskpart selhal: {error}";
                    return result;
                }

                result.Success = true;
                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("GPT partition created successfully for disk {DriveNumber}", driveNumber);
                }
            }
            finally
            {
                try { File.Delete(tempScriptPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<SanitizationPhaseResult> FormatNtfsAsync(
        string devicePath,
        string volumeLabel,
        CancellationToken cancellationToken)
    {
        var result = new SanitizationPhaseResult();

        try
        {
            // Wait for the volume to be recognized
            await Task.Delay(2000, cancellationToken);

            // Find the newly created volume
            var volumeLetter = await FindNewVolumeAsync(cancellationToken);
            if (volumeLetter == null)
            {
                result.ErrorMessage = "Nelze najít nový oddíl";
                return result;
            }

            // Format as NTFS
            var psi = new ProcessStartInfo
            {
                FileName = "format.com",
                Arguments = $"{volumeLetter}: /FS:NTFS /V:{volumeLabel} /Q /Y",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.ErrorMessage = "Nelze spustit format";
                return result;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                result.ErrorMessage = $"Format selhal: {error}";
                return result;
            }

            result.Success = true;
            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Volume {VolumeLetter} formatted as NTFS with label {Label}", 
                    volumeLetter, volumeLabel);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<string?> FindNewVolumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Use PowerShell to find volumes
            var script = @"
$volumes = Get-Volume | Where-Object { $_.DriveLetter -and $_.FileSystem -eq $null }
foreach ($vol in $volumes) {
    Write-Output $vol.DriveLetter
}
";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var letters = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return letters.FirstOrDefault()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private SafeFileHandle CreateFile(string path, uint access)
    {
        return Win32DiskInterop.CreateFile(
            path,
            access,
            0, // No sharing
            IntPtr.Zero,
            Win32DiskInterop.OPEN_EXISTING,
            Win32DiskInterop.FILE_ATTRIBUTE_NORMAL | Win32DiskInterop.FILE_FLAG_WRITE_THROUGH,
            IntPtr.Zero);
    }

    private async Task<int> WriteFileAsync(SafeFileHandle handle, byte[] buffer, int bytesToWrite, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var overlapped = IntPtr.Zero;
            if (!Win32DiskInterop.WriteFile(handle.DangerousGetHandle(), 
                Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0), 
                (uint)bytesToWrite, 
                out var bytesWritten, 
                overlapped))
            {
                Marshal.ThrowExceptionForHR(Marshal.GetLastWin32Error());
            }
            return (int)bytesWritten;
        }, cancellationToken);
    }

    private async Task<int> ReadFileAsync(SafeFileHandle handle, byte[] buffer, int bytesToRead, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            if (!Win32DiskInterop.ReadFile(handle.DangerousGetHandle(),
                Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0),
                (uint)bytesToRead,
                out var bytesRead,
                IntPtr.Zero))
            {
                Marshal.ThrowExceptionForHR(Marshal.GetLastWin32Error());
            }
            return (int)bytesRead;
        }, cancellationToken);
    }

    private sealed class SanitizationPhaseResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public long BytesWritten { get; set; }
        public long BytesRead { get; set; }
        public double SpeedMBps { get; set; }
        public int Errors { get; set; }
    }
}