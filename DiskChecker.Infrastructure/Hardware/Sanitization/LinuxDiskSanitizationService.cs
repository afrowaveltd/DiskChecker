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
    private const int DeviceOpenMaxAttempts = 10;
    private const int DeviceOpenRetryDelayMs = 250;

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
                PhaseKind = SanitizationProgressPhase.Write,
                Phase = "Zápis nul",
                ProgressPercent = 0,
                TotalBytes = diskSize
            });

            var writeResult = await WriteZerosAsync(normalizedPath, diskSize, progress, cancellationToken);
            result.ErrorDetails.AddRange(writeResult.ErrorDetails);
            if (!writeResult.Success)
            {
                result.ErrorMessage = writeResult.ErrorMessage;
                return result;
            }

            result.BytesWritten = writeResult.BytesWritten;
            result.WriteSpeedMBps = writeResult.SpeedMBps;
            result.ErrorsDetected += writeResult.Errors;

            await FlushDeviceAndDropCachesAsync(normalizedPath, cancellationToken);

            // Phase 3: Read and verify
            progress?.Report(new SanitizationProgress
            {
                PhaseKind = SanitizationProgressPhase.ReadVerify,
                Phase = "Čtení a ověření",
                ProgressPercent = 0,
                TotalBytes = diskSize
            });

            var readResult = await ReadAndVerifyAsync(normalizedPath, diskSize, progress, cancellationToken);
            result.ErrorDetails.AddRange(readResult.ErrorDetails);
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
                result.FileSystem = "ext4";
                result.VolumeLabel = volumeLabel;
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

        // Check for NVMe devices - extract controller number and namespace
        if (devicePath.Contains("nvme", StringComparison.OrdinalIgnoreCase))
        {
            // Parse pattern: nvme<controller>n<namespace>
            // e.g., /dev/nvme0n1 -> controller=0, namespace=1
            var nvmeMatch = System.Text.RegularExpressions.Regex.Match(
                devicePath, @"nvme(\d+)n(\d+)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (nvmeMatch.Success)
            {
                var controller = nvmeMatch.Groups[1].Value;
                var ns = nvmeMatch.Groups[2].Value;
                return $"/dev/nvme{controller}n{ns}";
            }
            // Fallback: try to extract just the controller number
            var digits = new string(devicePath.Where(char.IsDigit).ToArray());
            if (digits.Length > 0)
            {
                // First digit(s) before 'n' are the controller
                var nIndex = devicePath.IndexOf('n', StringComparison.OrdinalIgnoreCase);
                if (nIndex > 0)
                {
                    var controllerDigits = new string(devicePath[..nIndex].Where(char.IsDigit).ToArray());
                    var nsDigits = new string(devicePath[nIndex..].Where(char.IsDigit).ToArray());
                    return $"/dev/nvme{controllerDigits}n{nsDigits}";
                }
            }
            return "/dev/nvme0n1"; // Last resort fallback
        }

        // For SATA/SCSI drives - extract the drive letter
        // Pattern: /dev/sd[a-z], /dev/hd[a-z], /dev/vd[a-z]
        var driveMatch = System.Text.RegularExpressions.Regex.Match(
            devicePath, @"(sd|hd|vd)([a-z]+)$", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (driveMatch.Success)
        {
            return $"/dev/{driveMatch.Groups[1].Value}{driveMatch.Groups[2].Value}";
        }

        // If we have a PhysicalDrive pattern (Windows), convert to /dev/sdX
        if (devicePath.Contains("PhysicalDrive", StringComparison.OrdinalIgnoreCase))
        {
            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            var letter = (char)('a' + (int.TryParse(driveNumber, out var num) ? num : 0));
            return $"/dev/sd{letter}";
        }

        // Fallback - try to extract a number and map it
        var fallbackDigits = new string(devicePath.Where(char.IsDigit).ToArray());
        if (int.TryParse(fallbackDigits, out var diskNum) && diskNum < 26)
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
        // Keep the active methodology aligned with Windows: 64 MB zero-filled chunks,
        // write-through access, per-chunk progress, and one final measured result.
        return await WriteZerosFileStreamAsync(devicePath, diskSize, progress, cancellationToken);
    }

    /// <summary>
    /// Writes zeros using the same chunk size, speed smoothing, and progress model as Windows.
    /// </summary>
    private async Task<SanitizationPhaseResult> WriteZerosFileStreamAsync(
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
            using (var fileStream = await OpenDeviceStreamWithRetryAsync(
                devicePath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None,
                FileOptions.WriteThrough | FileOptions.SequentialScan,
                cancellationToken))
            {
                while (bytesWritten < diskSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var bytesToWrite = (int)Math.Min(BUFFER_SIZE, diskSize - bytesWritten);
                    var chunkStopwatch = Stopwatch.StartNew();

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesToWrite), cancellationToken);

                    chunkStopwatch.Stop();
                    bytesWritten += bytesToWrite;
                    // The stream uses FileOptions.WriteThrough for write-through semantics.
                    // Flush after each chunk can block indefinitely on some devices (USB, etc.)
                    // and skew timing measurements. Omitted to match Windows behavior.

                    if (result.Errors >= 10)
                    {
                        result.Success = false;
                        result.ErrorMessage = "Test byl ukončen: nalezeno 10 nebo více chyb. Disk je pravděpodobně vadný.";
                        return result;
                    }

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
                        PhaseKind = SanitizationProgressPhase.Write,
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
            result.ErrorDetails.Add(new SanitizationErrorDetail
            {
                Phase = "Write",
                ErrorCode = ex.HResult.ToString("X", System.Globalization.CultureInfo.InvariantCulture),
                Message = ex.Message,
                Details = ex.GetType().Name,
                OffsetBytes = bytesWritten
            });
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
        // Match Windows by measuring and verifying in the same single read pass.
        return await ReadAndVerifyFileStreamAsync(devicePath, diskSize, progress, cancellationToken);
    }

    /// <summary>
    /// Reads and verifies zeros in one pass, matching the Windows sanitization methodology.
    /// </summary>
    private async Task<SanitizationPhaseResult> ReadAndVerifyFileStreamAsync(
        string devicePath,
        long diskSize,
        IProgress<SanitizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new SanitizationPhaseResult();
        var stopwatch = Stopwatch.StartNew();
        var buffer = new byte[BUFFER_SIZE];
        long bytesRead = 0;
        double smoothedSpeed = 0;

        try
        {
            using (var fileStream = await OpenDeviceStreamWithRetryAsync(
                devicePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                FileOptions.SequentialScan,
                cancellationToken))
            {
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
                        result.ErrorDetails.Add(new SanitizationErrorDetail
                        {
                            Phase = "Read",
                            ErrorCode = "PARTIAL_READ",
                            Message = "Čtení neproběhlo v plné délce bloku.",
                            Details = $"Přečteno {bytesReadThisChunk} z očekávaných {bytesToRead} bajtů.",
                            OffsetBytes = bytesRead
                        });
                        if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
                        {
                            _logger.LogWarning("Read incomplete at offset {Offset}: read {Read} of {Expected}",
                                bytesRead, bytesReadThisChunk, bytesToRead);
                        }
                    }

                    var nonZeroCount = 0;
                    var firstNonZeroOffset = -1;
                    for (var i = 0; i < bytesReadThisChunk; i++)
                    {
                        if (buffer[i] != 0)
                        {
                            nonZeroCount++;
                            if (firstNonZeroOffset < 0)
                            {
                                firstNonZeroOffset = i;
                            }
                        }
                    }

                    if (nonZeroCount > 0)
                    {
                        result.Errors++;
                        result.ErrorDetails.Add(new SanitizationErrorDetail
                        {
                            Phase = "Verify",
                            ErrorCode = "NON_ZERO_DATA",
                            Message = "Ověření našlo nenulová data v přepsaném bloku.",
                            Details = $"Nenulových bajtů: {nonZeroCount}. První odchylka v rámci chunku na offsetu {firstNonZeroOffset}.",
                            OffsetBytes = bytesRead + Math.Max(firstNonZeroOffset, 0)
                        });
                        if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
                        {
                            _logger.LogWarning(
                                "Found {NonZeroCount} non-zero bytes in block at offset {Offset}",
                                nonZeroCount,
                                bytesRead + Math.Max(firstNonZeroOffset, 0));
                        }
                    }

                    if (result.Errors >= 10)
                    {
                        result.Success = false;
                        result.ErrorMessage = "Test byl ukončen: nalezeno 10 nebo více chyb. Disk je pravděpodobně vadný.";
                        return result;
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
                        PhaseKind = SanitizationProgressPhase.ReadVerify,
                        Phase = "Čtení a ověření",
                        ProgressPercent = (double)bytesRead / diskSize * 100,
                        BytesProcessed = bytesRead,
                        TotalBytes = diskSize,
                        CurrentSpeedMBps = smoothedSpeed,
                        Errors = result.Errors,
                        EstimatedTimeRemaining = eta
                    });
                }
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
            result.ErrorDetails.Add(new SanitizationErrorDetail
            {
                Phase = "Read",
                ErrorCode = ex.HResult.ToString("X", System.Globalization.CultureInfo.InvariantCulture),
                Message = ex.Message,
                Details = ex.GetType().Name,
                OffsetBytes = bytesRead
            });
            _logger?.LogError(ex, "Error reading/verifying {DevicePath}", devicePath);
        }

        return result;
    }

    private async Task FlushDeviceAndDropCachesAsync(string devicePath, CancellationToken cancellationToken)
    {
        try { await ExecuteCommandAsync("sync", string.Empty, cancellationToken); } catch { }
        try { await ExecuteCommandAsync("blockdev", $"--flushbufs \"{devicePath}\"", cancellationToken); } catch { }
        try { await File.WriteAllTextAsync("/proc/sys/vm/drop_caches", "3\n", cancellationToken); } catch { }
        try { await ExecuteCommandAsync("udevadm", "settle", cancellationToken); } catch { }
    }

    private async Task<FileStream> OpenDeviceStreamWithRetryAsync(
        string devicePath,
        FileMode mode,
        FileAccess access,
        FileShare share,
        FileOptions options,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    devicePath,
                    mode,
                    access,
                    share,
                    bufferSize: 65536,
                    options);
            }
            catch (IOException ex) when (attempt < DeviceOpenMaxAttempts)
            {
                _logger?.LogWarning(
                    ex,
                    "Device {DevicePath} is temporarily busy while opening for {Access}. Retrying ({Attempt}/{MaxAttempts})",
                    devicePath,
                    access,
                    attempt,
                    DeviceOpenMaxAttempts);
                await Task.Delay(DeviceOpenRetryDelayMs, cancellationToken);
            }
        }
    }

    private async Task<SanitizationPhaseResult> CreateGptPartitionAsync(
        string devicePath,
        CancellationToken cancellationToken)
    {
        var result = new SanitizationPhaseResult();

        try
        {
            await FlushDeviceAndDropCachesAsync(devicePath, cancellationToken);
            await ExecuteCommandAsync("wipefs", $"-a \"{devicePath}\"", cancellationToken);

            // First, use sfdisk (most portable). Omitting size lets sfdisk use all
            // remaining space; this is more compatible with older util-linux builds
            // and small legacy disks than passing size=0.
            var script = "label: gpt\nstart=2048, type=0FC63DAF-8483-4772-8E79-3D69D8477DE4\n";

            var sfdiskSuccess = false;
            string? sfdiskError = null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sfdisk",
                    Arguments = $"--force \"{devicePath}\"",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.StandardInput.WriteAsync(script);
                    process.StandardInput.Close();

                    var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                    var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                    await process.WaitForExitAsync(cancellationToken);
                    
                    var output = await outputTask;
                    var error = await errorTask;
                    
                    // sfdisk may return non-zero exit code for warnings that are not fatal
                    // Check if the partition table was actually written
                    if (process.ExitCode == 0 || 
                        error.Contains("The partition table has been altered", StringComparison.OrdinalIgnoreCase) ||
                        error.Contains("Syncing disks", StringComparison.OrdinalIgnoreCase))
                    {
                        sfdiskSuccess = true;
                    }
                    else
                    {
                        sfdiskError = $"sfdisk selhal (exit {process.ExitCode}): {error} {output}";
                    }
                }
                else
                {
                    sfdiskError = "Nelze spustit sfdisk";
                }
            }
            catch (Exception sfdiskEx)
            {
                sfdiskError = $"sfdisk vyhodil výjimku: {sfdiskEx.Message}";
            }

            // Fallback to parted if sfdisk failed
            if (!sfdiskSuccess)
            {
                if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("sfdisk failed ({Error}), falling back to parted", sfdiskError);

                try
                {
                    var partedPsi = new ProcessStartInfo
                    {
                        FileName = "parted",
                        Arguments = $"-s \"{devicePath}\" mklabel gpt mkpart primary ext4 1MiB 100%",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var partedProcess = Process.Start(partedPsi);
                    if (partedProcess != null)
                    {
                        var partedErrorTask = partedProcess.StandardError.ReadToEndAsync(cancellationToken);
                        await partedProcess.WaitForExitAsync(cancellationToken);
                        var partedError = await partedErrorTask;

                        if (partedProcess.ExitCode != 0)
                        {
                            result.ErrorMessage = $"Vytvoření GPT oddílu selhalo (sfdisk i parted). sfdisk: {sfdiskError}. parted (exit {partedProcess.ExitCode}): {partedError}";
                            return result;
                        }

                        sfdiskSuccess = true;
                        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                            _logger.LogInformation("GPT partition created via parted fallback for {DevicePath}", devicePath);
                    }
                    else
                    {
                        result.ErrorMessage = "Nelze spustit sfdisk ani parted pro vytvoření oddílu.";
                        return result;
                    }
                }
                catch (Exception partedEx)
                {
                    result.ErrorMessage = $"Vytvoření GPT oddílu selhalo. sfdisk: {sfdiskError}. parted: {partedEx.Message}";
                    return result;
                }
            }

            await FlushDeviceAndDropCachesAsync(devicePath, cancellationToken);

            // Notify kernel of partition changes - try partprobe first, fallback to blockdev
            try
            {
                await ExecuteCommandAsync("partprobe", devicePath, cancellationToken);
            }
            catch
            {
                try
                {
                    await ExecuteCommandAsync("blockdev", $"--rereadpt \"{devicePath}\"", cancellationToken);
                }
                catch
                {
                    _logger?.LogWarning("Neither partprobe nor blockdev available for partition table reload");
                }
            }

            await ExecuteCommandAsync("udevadm", "settle", cancellationToken);

            // Wait for partition to appear - use polling with timeout
            var partitionPath = GetPartitionPath(devicePath);
            var partitionAppeared = await WaitForPartitionAsync(partitionPath, TimeSpan.FromSeconds(15), cancellationToken);
            
            if (!partitionAppeared)
            {
                _logger?.LogWarning("Partition {PartitionPath} did not appear within timeout; will retry detection in format step", partitionPath);
                // Don't fail - FormatExt4Async has its own detection logic
            }

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

    /// <summary>
    /// Determines the expected partition path for a given device.
    /// /dev/sda → /dev/sda1, /dev/nvme0n1 → /dev/nvme0n1p1, /dev/mmcblk0 → /dev/mmcblk0p1
    /// </summary>
    private static string GetPartitionPath(string devicePath)
    {
        if (devicePath.Contains("nvme", StringComparison.OrdinalIgnoreCase))
            return devicePath + "p1";
        if (devicePath.Contains("mmcblk", StringComparison.OrdinalIgnoreCase))
            return devicePath + "p1";
        // SATA/SCSI/VirtIO: /dev/sda → /dev/sda1, /dev/vda → /dev/vda1
        return devicePath + "1";
    }

    /// <summary>
    /// Polls for a partition device file to appear, with timeout.
    /// </summary>
    private static async Task<bool> WaitForPartitionAsync(string partitionPath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (File.Exists(partitionPath))
                return true;
            
            await Task.Delay(250, cancellationToken);
        }
        return File.Exists(partitionPath);
    }

    private async Task<SanitizationPhaseResult> FormatExt4Async(
        string devicePath,
        string volumeLabel,
        CancellationToken cancellationToken)
    {
        var result = new SanitizationPhaseResult();

        try
        {
            // Determine the partition device name using the shared helper
            string partitionPath = GetPartitionPath(devicePath);

            // Verify partition exists - if not, try to find it via sysfs
            if (!File.Exists(partitionPath))
            {
                var deviceName = Path.GetFileName(devicePath);
                var sysfsPath = $"/sys/block/{deviceName}";
                
                if (Directory.Exists(sysfsPath))
                {
                    // Look for partition directories: sda1, nvme0n1p1, etc.
                    var entries = Directory.GetFileSystemEntries(sysfsPath)
                        .Select(p => Path.GetFileName(p))
                        .Where(n => n.StartsWith(deviceName, StringComparison.Ordinal) && n != deviceName)
                        .OrderBy(n => n)
                        .ToList();

                    if (entries.Count > 0)
                    {
                        partitionPath = "/dev/" + entries[0];
                        if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                            _logger.LogInformation("Found partition via sysfs: {PartitionPath}", partitionPath);
                    }
                }
                
                // If still not found, try lsblk as last resort
                if (!File.Exists(partitionPath))
                {
                    try
                    {
                        var lsblkOutput = await ExecuteCommandAndGetOutputAsync("lsblk", $"-ln -o NAME {devicePath}", cancellationToken);
                        if (!string.IsNullOrWhiteSpace(lsblkOutput))
                        {
                            var lines = lsblkOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            // First line is the device itself, subsequent lines are partitions
                            if (lines.Length > 1)
                            {
                                partitionPath = "/dev/" + lines[1].Trim();
                            }
                        }
                    }
                    catch
                    {
                        // Ignore lsblk errors
                    }
                }
            }

            if (!File.Exists(partitionPath))
            {
                result.ErrorMessage = $"Oddíl pro formátování nebyl nalezen (očekáván: {partitionPath})";
                _logger?.LogError("Partition not found for formatting: expected {Expected}", partitionPath);
                return result;
            }

            // Format as ext4 with force flag to skip confirmation
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

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                result.ErrorMessage = $"mkfs.ext4 selhal (exit {process.ExitCode}): {error}";
                _logger?.LogError("mkfs.ext4 failed for {PartitionPath}: {Error}", partitionPath, error);
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

    private async Task<string?> ExecuteCommandAndGetOutputAsync(string command, string arguments, CancellationToken cancellationToken)
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
            if (process == null) return null;

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return await outputTask;
        }
        catch
        {
            return null;
        }
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
        public List<SanitizationErrorDetail> ErrorDetails { get; } = new();
    }
}
