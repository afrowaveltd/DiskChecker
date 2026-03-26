using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware.Sanitization;

/// <summary>
/// Linux implementation of disk sanitization service.
/// Uses direct I/O to /dev/sdX devices for secure disk wiping.
/// WARNING: This destroys ALL data on the disk!
/// </summary>
public class LinuxDiskSanitizationService : IDiskSanitizationService
{
    private readonly ILogger<LinuxDiskSanitizationService>? _logger;
    private const int BUFFER_SIZE = 64 * 1024 * 1024; // 64 MB buffer

    public LinuxDiskSanitizationService(ILogger<LinuxDiskSanitizationService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<SanitizationResult> SanitizeDiskAsync(
        string devicePath,
        long diskSize,
        bool createPartition = true,
        bool format = true,
        string volumeLabel = "Sanitized",
        IProgress<SanitizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new SanitizationResult();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Normalize device path - ensure it's in /dev/sdX or /dev/nvme format
            var normalizedPath = NormalizeDevicePath(devicePath);
            
            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Starting disk sanitization for {DevicePath}", normalizedPath);
            }

            // Verify we have root privileges
            if (!await VerifyRootPrivilegesAsync())
            {
                result.ErrorMessage = "Sanitizace vyžaduje root práva. Spusťte aplikaci s sudo.";
                return result;
            }

            // Verify device exists
            if (!File.Exists(normalizedPath))
            {
                result.ErrorMessage = $"Zařízení neexistuje: {normalizedPath}";
                return result;
            }

            // Phase 1: Unmount all partitions on the device
            progress?.Report(new SanitizationProgress
            {
                Phase = "Odpojování oddílů",
                ProgressPercent = 0
            });

            await UnmountAllPartitionsAsync(normalizedPath, cancellationToken);

            // Phase 2: Write zeros
            progress?.Report(new SanitizationProgress
            {
                Phase = "Zápis nul",
                ProgressPercent = 0,
                TotalBytes = diskSize
            });

            var writeResult = await WriteZerosAsync(normalizedPath, diskSize, progress, cancellationToken);
            if (!writeResult.Success)
            {
                result.ErrorMessage = writeResult.ErrorMessage;
                return result;
            }

            result.BytesWritten = writeResult.BytesWritten;
            result.WriteSpeedMBps = writeResult.SpeedMBps;
            result.ErrorsDetected += writeResult.Errors;

            // Phase 3: Read and verify
            progress?.Report(new SanitizationProgress
            {
                Phase = "Čtení a ověření",
                ProgressPercent = 0,
                TotalBytes = diskSize
            });

            var readResult = await ReadAndVerifyAsync(normalizedPath, diskSize, progress, cancellationToken);
            if (!readResult.Success)
            {
                result.ErrorMessage = readResult.ErrorMessage;
                return result;
            }

            result.BytesRead = readResult.BytesRead;
            result.ReadSpeedMBps = readResult.SpeedMBps;
            result.ErrorsDetected += readResult.Errors;

            // Phase 4: Create GPT partition
            if (createPartition)
            {
                progress?.Report(new SanitizationProgress
                {
                    Phase = "Vytváření GPT oddílu",
                    ProgressPercent = 0
                });

                var partitionResult = await CreateGptPartitionAsync(normalizedPath, cancellationToken);
                if (!partitionResult.Success)
                {
                    result.ErrorMessage = partitionResult.ErrorMessage;
                    return result;
                }
                result.PartitionCreated = true;
            }

            // Phase 5: Format (ext4 on Linux)
            if (format && createPartition)
            {
                progress?.Report(new SanitizationProgress
                {
                    Phase = "Formátování ext4",
                    ProgressPercent = 0
                });

                var formatResult = await FormatExt4Async(normalizedPath, volumeLabel, cancellationToken);
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
                    normalizedPath, result.Duration);
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

    private string NormalizeDevicePath(string devicePath)
    {
        // If path is already a valid /dev path, return it
        if (devicePath.StartsWith("/dev/", StringComparison.Ordinal))
            return devicePath;

        // Try to extract drive identifier
        var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
        
        // Check for NVMe devices
        if (devicePath.Contains("nvme", StringComparison.OrdinalIgnoreCase))
        {
            // For NVMe drives, use the pattern /dev/nvme0n1
            var nvmeIndex = int.TryParse(driveNumber, out var num) ? num : 0;
            return $"/dev/nvme{nvmeIndex}n1";
        }

        // For SATA/SCSI drives
        // Extract letter if present (e.g., /dev/sda -> 'a')
        var letters = devicePath.Where(char.IsLetter).ToArray();
        if (letters.Length > 0)
        {
            // Find the last lowercase letter which should be the drive letter
            for (int i = letters.Length - 1; i >= 0; i--)
            {
                if (char.IsLower(letters[i]))
                {
                    return $"/dev/sd{letters[i]}";
                }
            }
        }

        // If we have a PhysicalDrive pattern (Windows), convert to /dev/sdX
        if (devicePath.Contains("PhysicalDrive", StringComparison.OrdinalIgnoreCase))
        {
            // Map PhysicalDrive0 -> /dev/sda, PhysicalDrive1 -> /dev/sdb, etc.
            var letter = (char)('a' + (int.TryParse(driveNumber, out var num) ? num : 0));
            return $"/dev/sd{letter}";
        }

        // Fallback - try to extract a number and map it
        if (int.TryParse(driveNumber, out var diskNum))
        {
            var letter = (char)('a' + diskNum);
            return $"/dev/sd{letter}";
        }

        return devicePath;
    }

    private async Task<bool> VerifyRootPrivilegesAsync()
    {
        try
        {
            // Check if running as root (UID 0)
            var psi = new ProcessStartInfo
            {
                FileName = "id",
                Arguments = "-u",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output.Trim() == "0";
        }
        catch
        {
            return false;
        }
    }

    private async Task UnmountAllPartitionsAsync(string devicePath, CancellationToken cancellationToken)
    {
        try
        {
            // Get all partitions of this device
            // For /dev/sda, partitions are /dev/sda1, /dev/sda2, etc.
            // For /dev/nvme0n1, partitions are /dev/nvme0n1p1, /dev/nvme0n1p2, etc.
            
            var deviceName = Path.GetFileName(devicePath);
            var partitionsPath = "/sys/block/" + deviceName + "/holders";
            
            // Find mounted partitions using findmnt
            var psi = new ProcessStartInfo
            {
                FileName = "findmnt",
                Arguments = $"-l -o TARGET,SOURCE | grep '{devicePath}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            // Parse mounted partitions and unmount them
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1)
                {
                    var mountPoint = parts[0];
                    await ExecuteCommandAsync("umount", $"-f \"{mountPoint}\"", cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to unmount partitions for {DevicePath}", devicePath);
            // Continue anyway - we'll try to write directly
        }
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
        double smoothedSpeed = 0;

        try
        {
            // Open device for writing with direct I/O (O_DIRECT) to bypass cache
            // We use dd command for reliability with direct I/O
            using var fileStream = new FileStream(
                devicePath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 65536,
                options: FileOptions.WriteThrough | FileOptions.SequentialScan);

            // Write zeros in chunks
            while (bytesWritten < diskSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytesToWrite = (int)Math.Min(BUFFER_SIZE, diskSize - bytesWritten);
                var chunkStopwatch = Stopwatch.StartNew();

                // Use async write for better performance
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesToWrite), cancellationToken);

                chunkStopwatch.Stop();
                bytesWritten += bytesToWrite;

                var chunkSeconds = chunkStopwatch.Elapsed.TotalSeconds;
                var instantSpeed = chunkSeconds > 0
                    ? bytesToWrite / (1024.0 * 1024.0) / chunkSeconds
                    : 0;
                smoothedSpeed = smoothedSpeed <= 0 ? instantSpeed : (smoothedSpeed * 0.7) + (instantSpeed * 0.3);

                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var averageSpeed = elapsed > 0 ? bytesWritten / (1024.0 * 1024.0) / elapsed : 0;
                var remaining = diskSize - bytesWritten;
                var eta = averageSpeed > 0 ? TimeSpan.FromSeconds(remaining / (1024.0 * 1024.0) / averageSpeed) : (TimeSpan?)null;

                progress?.Report(new SanitizationProgress
                {
                    Phase = "Zápis nul",
                    ProgressPercent = (double)bytesWritten / diskSize * 100,
                    BytesProcessed = bytesWritten,
                    TotalBytes = diskSize,
                    CurrentSpeedMBps = smoothedSpeed,
                    Errors = result.Errors,
                    EstimatedTimeRemaining = eta
                });
            }

            await fileStream.FlushAsync(cancellationToken);

            result.Success = true;
            result.BytesWritten = bytesWritten;
            result.SpeedMBps = stopwatch.Elapsed.TotalSeconds > 0
                ? bytesWritten / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds
                : 0;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger?.LogError(ex, "Error writing zeros to {DevicePath}", devicePath);
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
        var zeroBuffer = new byte[BUFFER_SIZE];
        long bytesRead = 0;
        double smoothedSpeed = 0;

        try
        {
            using var fileStream = new FileStream(
                devicePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 65536,
                options: FileOptions.SequentialScan);

            // Read and verify all zeros
            while (bytesRead < diskSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytesToRead = (int)Math.Min(BUFFER_SIZE, diskSize - bytesRead);
                var chunkStopwatch = Stopwatch.StartNew();
                var bytesReadThisChunk = await fileStream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);
                chunkStopwatch.Stop();

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
                for (int i = 0; i < bytesReadThisChunk; i++)
                {
                    if (buffer[i] != 0)
                    {
                        result.Errors++;
                        // Only log first few errors
                        if (result.Errors <= 10 && _logger != null && _logger.IsEnabled(LogLevel.Warning))
                        {
                            _logger.LogWarning("Non-zero byte found at offset {Offset}", bytesRead + i);
                        }
                    }
                }

                bytesRead += bytesReadThisChunk;

                var chunkSeconds = chunkStopwatch.Elapsed.TotalSeconds;
                var instantSpeed = chunkSeconds > 0
                    ? bytesReadThisChunk / (1024.0 * 1024.0) / chunkSeconds
                    : 0;
                smoothedSpeed = smoothedSpeed <= 0 ? instantSpeed : (smoothedSpeed * 0.7) + (instantSpeed * 0.3);

                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var averageSpeed = elapsed > 0 ? bytesRead / (1024.0 * 1024.0) / elapsed : 0;
                var remaining = diskSize - bytesRead;
                var eta = averageSpeed > 0 ? TimeSpan.FromSeconds(remaining / (1024.0 * 1024.0) / averageSpeed) : (TimeSpan?)null;

                progress?.Report(new SanitizationProgress
                {
                    Phase = "Čtení a ověření",
                    ProgressPercent = (double)bytesRead / diskSize * 100,
                    BytesProcessed = bytesRead,
                    TotalBytes = diskSize,
                    CurrentSpeedMBps = smoothedSpeed,
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
            _logger?.LogError(ex, "Error reading/verifying {DevicePath}", devicePath);
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
            // Use sfdisk to create GPT partition table with a single partition
            var script = @"label: gpt
unit: sectors

start=2048, type=0FC63DAF-8483-4772-8E79-3D69D8477DE4, bootable
";

            var psi = new ProcessStartInfo
            {
                FileName = "sfdisk",
                Arguments = $"\"{devicePath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.ErrorMessage = "Nelze spustit sfdisk";
                return result;
            }

            await process.StandardInput.WriteAsync(script);
            process.StandardInput.Close();

            // Read output and wait for exit in parallel to avoid deadlock
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                result.ErrorMessage = $"sfdisk selhal: {error}";
                return result;
            }

            // Notify kernel of partition changes
            await ExecuteCommandAsync("partprobe", devicePath, cancellationToken);

            // Wait for partition to appear
            await Task.Delay(1000, cancellationToken);

            result.Success = true;
            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("GPT partition created successfully for {DevicePath}", devicePath);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger?.LogError(ex, "Error creating GPT partition on {DevicePath}", devicePath);
        }

        return result;
    }

    private async Task<SanitizationPhaseResult> FormatExt4Async(
        string devicePath,
        string volumeLabel,
        CancellationToken cancellationToken)
    {
        var result = new SanitizationPhaseResult();

        try
        {
            // Determine the partition device name
            // For /dev/sda -> /dev/sda1
            // For /dev/nvme0n1 -> /dev/nvme0n1p1
            string partitionPath;
            if (devicePath.Contains("nvme"))
            {
                // NVMe devices: /dev/nvme0n1 -> /dev/nvme0n1p1
                partitionPath = devicePath + "p1";
            }
            else if (devicePath.Contains("mmcblk"))
            {
                // MMC/SD cards: /dev/mmcblk0 -> /dev/mmcblk0p1
                partitionPath = devicePath + "p1";
            }
            else
            {
                // SATA/SCSI: /dev/sda -> /dev/sda1
                partitionPath = devicePath + "1";
            }

            // Verify partition exists
            if (!File.Exists(partitionPath))
            {
                // Try to find any partition on this device
                var deviceName = Path.GetFileName(devicePath);
                var partitionsPath = $"/sys/block/{deviceName}";
                if (Directory.Exists(partitionsPath))
                {
                    var partitions = Directory.GetDirectories(partitionsPath)
                        .Where(d => System.IO.Path.GetFileName(d).StartsWith(deviceName, StringComparison.Ordinal))
                        .ToList();

                    if (partitions.Count > 0)
                    {
                        partitionPath = "/dev/" + System.IO.Path.GetFileName(partitions[0]);
                    }
                }
            }

            // Format as ext4
            var psi = new ProcessStartInfo
            {
                FileName = "mkfs.ext4",
                Arguments = $"-F -L \"{volumeLabel}\" \"{partitionPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.ErrorMessage = "Nelze spustit mkfs.ext4";
                return result;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                result.ErrorMessage = $"mkfs.ext4 selhal: {error}";
                return result;
            }

            result.Success = true;
            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Partition {PartitionPath} formatted as ext4 with label {Label}",
                    partitionPath, volumeLabel);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger?.LogError(ex, "Error formatting partition");
        }

        return result;
    }

    private async Task ExecuteCommandAsync(string command, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch
        {
            // Ignore errors for utility commands
        }
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