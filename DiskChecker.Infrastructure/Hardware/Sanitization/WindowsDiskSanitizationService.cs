using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace DiskChecker.Infrastructure.Hardware.Sanitization;

/// <summary>
/// Windows implementation of disk sanitization service.
/// Uses Win32 API for raw disk access.
/// WARNING: This destroys ALL data on the disk!
/// </summary>
public class WindowsDiskSanitizationService : IDiskSanitizationService
{
    private readonly ILogger<WindowsDiskSanitizationService>? _logger;
    private const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x70140;
    private const int BUFFER_SIZE = 64 * 1024 * 1024; // 64 MB buffer

    public WindowsDiskSanitizationService(ILogger<WindowsDiskSanitizationService>? logger = null)
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
        bool format = true,
        string volumeLabel = "SCCM",
        IProgress<SanitizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new SanitizationResult();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Phase 0: Verify administrator privileges
            progress?.Report(new SanitizationProgress
            {
                Phase = "Kontrola oprávnění",
                ProgressPercent = 0
            });

            if (!await VerifyAdministratorPrivilegesAsync())
            {
                result.ErrorMessage = "Sanitizace vyžaduje správcovská práva. Spusťte aplikaci jako správce.";
                return result;
            }

            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Starting disk sanitization for {DevicePath}", devicePath);
            }

            // Phase 1: Dismount all volumes on the disk
            progress?.Report(new SanitizationProgress
            {
                Phase = "Odpojování svazků",
                ProgressPercent = 0
            });

            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            var dismountResult = await DismountAllVolumesAsync(driveNumber, cancellationToken);
            if (!dismountResult.Success)
            {
                result.ErrorMessage = dismountResult.ErrorMessage;
                return result;
            }

            // Phase 2: Write zeros
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

            // Phase 3: Read and verify
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

            // Phase 4: Create GPT partition
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
                result.Formatted = partitionResult.PartitionFormatted; // Set if diskpart formatted it
            }

            // Phase 5: Format NTFS (only if not already formatted by diskpart)
            if (format && createPartition && !result.Formatted)
            {
                progress?.Report(new SanitizationProgress
                {
                    Phase = "Formátování NTFS",
                    ProgressPercent = 0
                });

                var formatResult = await FormatNtfsAsync(devicePath, volumeLabel, cancellationToken);
                if (!formatResult.Success)
                {
                    // Non-critical error - sanitization itself was successful
                    _logger?.LogWarning("Formatting failed, but partition was created: {Error}", formatResult.ErrorMessage);
                    result.ErrorMessage = formatResult.ErrorMessage;
                    return result;
                }
                result.Formatted = true;
            }
            else if (format && createPartition && result.Formatted)
            {
                // Already formatted by diskpart, just log it
                _logger?.LogInformation("Skipping separate format step - partition was already formatted by diskpart");
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

    /// <summary>
    /// Verify that the process is running with administrator privileges.
    /// Uses Windows API directly for reliable detection.
    /// </summary>
#pragma warning disable CA1416 // Platform compatibility - this is Windows-only code
    private bool IsRunningAsAdministrator()
    {
        try
        {
            // Use Windows API directly - much more reliable than PowerShell
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to verify administrator privileges");
            return false;
        }
    }
#pragma warning restore CA1416

    /// <summary>
    /// Verify that the process is running with administrator privileges.
    /// </summary>
    private Task<bool> VerifyAdministratorPrivilegesAsync()
    {
        return Task.FromResult(IsRunningAsAdministrator());
    }

    /// <summary>
    /// Dismount all volumes on the specified disk using diskpart.
    /// </summary>
    private async Task<SanitizationPhaseResult> DismountAllVolumesAsync(
        string driveNumber,
        CancellationToken cancellationToken)
    {
        var result = new SanitizationPhaseResult();

        try
        {
            // Use diskpart to offline the disk and clear read-only attributes
            var diskpartScript = $@"select disk {driveNumber}
attributes disk clear readonly
offline disk";

            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"diskpart_dismount_{Guid.NewGuid():N}.txt");
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
                    CreateNoWindow = true
                    // NOTE: Removed Verb = "runas" - app should already be running as admin
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    result.ErrorMessage = "Nelze spustit diskpart pro odpojení svazků";
                    return result;
                }

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    // Log warning but continue - we'll try to open the disk anyway
                    _logger?.LogWarning("diskpart offline warning (ExitCode={ExitCode}): {Error}", 
                        process.ExitCode, error);
                }

                // Now turn the disk back online for writing
                await Task.Delay(500, cancellationToken); // Brief delay before re-onlining

                var onlineScript = $@"select disk {driveNumber}
online disk";
                await File.WriteAllTextAsync(tempScriptPath, onlineScript, cancellationToken);

                var psiOnline = new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = $"/s \"{tempScriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var processOnline = Process.Start(psiOnline);
                if (processOnline != null)
                {
                    await processOnline.WaitForExitAsync(cancellationToken);
                }

                result.Success = true;
                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Successfully dismounted and re-onlined disk {DriveNumber}", driveNumber);
                }
            }
            finally
            {
                try { File.Delete(tempScriptPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to dismount volumes on disk {DriveNumber}", driveNumber);
            result.ErrorMessage = $"Nepodařilo se odpojit svazky: {ex.Message}";
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
        double smoothedSpeed = 0;

        SafeFileHandle? handle = null;

        try
        {
            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            var physicalPath = $@"\\.\PhysicalDrive{driveNumber}";

            // Open handle with exclusive access
            handle = CreateFileExclusive(physicalPath);
            if (handle == null || handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                result.ErrorMessage = $"Nelze získat výhradní přístup k disku {physicalPath}. Chyba: {error} ({GetWin32ErrorMessage(error)})";
                return result;
            }

            // Write zeros in chunks
            while (bytesWritten < diskSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytesToWrite = (int)Math.Min(BUFFER_SIZE, diskSize - bytesWritten);
                var chunkStopwatch = Stopwatch.StartNew();
                var bytesWrittenThisChunk = await WriteFileAsync(handle, buffer, bytesToWrite, cancellationToken);
                chunkStopwatch.Stop();
                
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

                var chunkSeconds = chunkStopwatch.Elapsed.TotalSeconds;
                var instantSpeed = chunkSeconds > 0
                    ? bytesWrittenThisChunk / (1024.0 * 1024.0) / chunkSeconds
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

            result.Success = true;
            result.BytesWritten = bytesWritten;
            result.SpeedMBps = stopwatch.Elapsed.TotalSeconds > 0 
                ? bytesWritten / (1024.0 * 1024.0) / stopwatch.Elapsed.TotalSeconds 
                : 0;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger?.LogError(ex, "Error writing zeros to disk");
        }
        finally
        {
            handle?.Dispose();
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
        double smoothedSpeed = 0;

        SafeFileHandle? handle = null;

        try
        {
            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            var physicalPath = $@"\\.\PhysicalDrive{driveNumber}";

            handle = CreateFileExclusive(physicalPath);
            if (handle == null || handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                result.ErrorMessage = $"Nelze otevřít disk pro čtení: {physicalPath}. Chyba: {error} ({GetWin32ErrorMessage(error)})";
                return result;
            }

            // Read and verify all zeros
            while (bytesRead < diskSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytesToRead = (int)Math.Min(BUFFER_SIZE, diskSize - bytesRead);
                var chunkStopwatch = Stopwatch.StartNew();
                var bytesReadThisChunk = await ReadFileAsync(handle, buffer, bytesToRead, cancellationToken);
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
                foreach (var b in buffer.AsSpan(0, bytesReadThisChunk))
                {
                    if (b != 0)
                    {
                        result.Errors++;
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
            _logger?.LogError(ex, "Error reading/verifying disk");
        }
        finally
        {
            handle?.Dispose();
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
            
            // Use diskpart to create GPT partition and format it directly
            // This ensures the partition is immediately usable
            var diskpartScript = $@"select disk {driveNumber}
clean
convert gpt
create partition primary
format quick fs=ntfs label=""SCCM""
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
                    CreateNoWindow = true
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

                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("diskpart partition output:\n{Output}", output);
                }

                if (process.ExitCode != 0 && !output.Contains("successfully", StringComparison.OrdinalIgnoreCase))
                {
                    result.ErrorMessage = $"diskpart selhal (exit {process.ExitCode}): {error}";
                    return result;
                }

                result.Success = true;
                result.PartitionFormatted = true; // diskpart already formatted the partition
                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("GPT partition created and formatted successfully for disk {DriveNumber}", driveNumber);
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
            _logger?.LogError(ex, "Error creating GPT partition");
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
            var driveNumber = new string(devicePath.Where(char.IsDigit).ToArray());
            
            // First, ensure the partition has a drive letter assigned
            // diskpart create partition primary doesn't always assign a letter automatically
            var assignScript = $@"select disk {driveNumber}
select partition 1
assign";

            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"diskpart_assign_{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(tempScriptPath, assignScript, cancellationToken);

                var psiAssign = new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = $"/s \"{tempScriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var processAssign = Process.Start(psiAssign);
                if (processAssign != null)
                {
                    var assignOutput = await processAssign.StandardOutput.ReadToEndAsync(cancellationToken);
                    var assignError = await processAssign.StandardError.ReadToEndAsync(cancellationToken);
                    await processAssign.WaitForExitAsync(cancellationToken);
                    
                    if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("diskpart assign output: {Output}, error: {Error}", assignOutput, assignError);
                    }
                }
            }
            finally
            {
                try { File.Delete(tempScriptPath); } catch { }
            }

            // Wait for the volume to be recognized by Windows
            await Task.Delay(3000, cancellationToken);

            // Find the newly created volume - try multiple methods
            var volumeLetter = await FindNewVolumeAsync(driveNumber, cancellationToken);
            
            if (volumeLetter == null)
            {
                // If we still can't find it, try to refresh disk information and wait longer
                _logger?.LogWarning("Could not find volume immediately, attempting refresh...");
                
                // Force disk rescan
                var rescanProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = "/s -", // Interactive mode to run rescan
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                
                if (rescanProcess != null)
                {
                    await rescanProcess.StandardInput.WriteLineAsync("rescan");
                    await rescanProcess.StandardInput.WriteLineAsync("exit");
                    rescanProcess.StandardInput.Close();
                    await rescanProcess.WaitForExitAsync(cancellationToken);
                }
                
                await Task.Delay(2000, cancellationToken);
                volumeLetter = await FindNewVolumeAsync(driveNumber, cancellationToken);
            }

            if (volumeLetter == null)
            {
                // Volume letter assignment failed, but partition was created successfully
                // This is not a critical error - the disk is sanitized and has a partition
                _logger?.LogWarning("Could not find volume letter, but partition exists. Manual formatting may be required.");
                result.Success = true; // Mark as success - sanitization itself is complete
                result.ErrorMessage = "Oddíl vytvořen, ale nepodařilo se přiřadit písmeno. Zkuste manuálně přiřadit písmeno ve Správci disků.";
                return result;
            }

            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Found volume letter: {VolumeLetter}", volumeLetter);
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
            _logger?.LogError(ex, "Error formatting partition");
        }

        return result;
    }

    private async Task<string?> FindNewVolumeAsync(string driveNumber, CancellationToken cancellationToken)
    {
        try
        {
            // Method 1: Use PowerShell to find volumes on the specific disk
            var script = $@"
$disk = Get-Disk -Number {driveNumber} -ErrorAction SilentlyContinue
if ($disk -and $disk.PartitionStyle -eq 'GPT') {{
    $partitions = Get-Partition -DiskNumber {driveNumber} -ErrorAction SilentlyContinue
    foreach ($part in $partitions) {{
        if ($part.DriveLetter -and $part.DriveLetter -ne '') {{
            Write-Output $part.DriveLetter
        }}
    }}
}}
";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{script.Trim().Replace("\r", "").Replace("\n", " ")}\"",
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

            if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("FindNewVolume PowerShell output: {Output}, error: {Error}", output, error);
            }

            var letters = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var letter = letters.FirstOrDefault()?.Trim();
            
            if (!string.IsNullOrEmpty(letter))
            {
                return letter;
            }

            // Method 2: Use diskpart to list volumes and find unformatted ones
            var diskpartScript = $@"list volume";
            var tempScriptPath = Path.Combine(Path.GetTempPath(), $"diskpart_list_{Guid.NewGuid():N}.txt");
            
            try
            {
                await File.WriteAllTextAsync(tempScriptPath, diskpartScript, cancellationToken);

                var psiDp = new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = $"/s \"{tempScriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var processDp = Process.Start(psiDp);
                if (processDp == null) return null;

                var dpOutput = await processDp.StandardOutput.ReadToEndAsync(cancellationToken);
                await processDp.WaitForExitAsync(cancellationToken);

                if (_logger != null && _logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("diskpart list volume output:\n{Output}", dpOutput);
                }

                // Parse output to find RAW volumes (no filesystem)
                // Format: Volume ###  Lttr  Label  Fs Type    Size     Status
                // Example: Volume 1     E           RAW    Primary  500 GB  Healthy
                var lines = dpOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("RAW") || line.Contains("Ostatní") || line.Contains("Unknown"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            var potentialLetter = parts[2].Trim();
                            if (potentialLetter.Length == 1 && char.IsLetter(potentialLetter[0]))
                            {
                                return potentialLetter;
                            }
                        }
                    }
                }
            }
            finally
            {
                try { File.Delete(tempScriptPath); } catch { }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error finding new volume");
            return null;
        }
    }

    /// <summary>
    /// Create file handle with exclusive access (no sharing) for raw disk operations.
    /// </summary>
    private SafeFileHandle CreateFileExclusive(string path)
    {
        return Win32DiskInterop.CreateFile(
            path,
            Win32DiskInterop.GENERIC_READ | Win32DiskInterop.GENERIC_WRITE,
            0, // No sharing - exclusive access
            IntPtr.Zero,
            Win32DiskInterop.OPEN_EXISTING,
            Win32DiskInterop.FILE_ATTRIBUTE_NORMAL | Win32DiskInterop.FILE_FLAG_WRITE_THROUGH,
            IntPtr.Zero);
    }

    private async Task<int> WriteFileAsync(SafeFileHandle handle, byte[] buffer, int bytesToWrite, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            // Use GCHandle to safely pin the buffer for the duration of the write
            var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var bufferPtr = bufferHandle.AddrOfPinnedObject();
                if (!Win32DiskInterop.WriteFile(handle.DangerousGetHandle(),
                    bufferPtr,
                    (uint)bytesToWrite,
                    out var bytesWritten,
                    IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    // Don't throw on partial writes - return what was actually written
                    if (error == 0 && bytesWritten > 0)
                    {
                        return (int)bytesWritten;
                    }
                    Marshal.ThrowExceptionForHR(error);
                }
                return (int)bytesWritten;
            }
            finally
            {
                bufferHandle.Free();
            }
        }, cancellationToken);
    }

    private async Task<int> ReadFileAsync(SafeFileHandle handle, byte[] buffer, int bytesToRead, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            // Use GCHandle to safely pin the buffer for the duration of the read
            var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var bufferPtr = bufferHandle.AddrOfPinnedObject();
                if (!Win32DiskInterop.ReadFile(handle.DangerousGetHandle(),
                    bufferPtr,
                    (uint)bytesToRead,
                    out var bytesRead,
                    IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    // Don't throw on partial reads - return what was actually read
                    if (error == 0 && bytesRead > 0)
                    {
                        return (int)bytesRead;
                    }
                    Marshal.ThrowExceptionForHR(error);
                }
                return (int)bytesRead;
            }
            finally
            {
                bufferHandle.Free();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Convert Win32 error code to human-readable message.
    /// </summary>
    private static string GetWin32ErrorMessage(int errorCode)
    {
        return errorCode switch
        {
            5 => "Přístup odepřen (ACCESS_DENIED) - spusťte jako správce",
            32 => "Soubor je používán jiným procesem (SHARING_VIOLATION)",
            87 => "Neplatný parametr",
            1117 => "Požadavek nebyl proveden",
            _ => $"Chyba kód: {errorCode}"
        };
    }

    private sealed class SanitizationPhaseResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public long BytesWritten { get; set; }
        public long BytesRead { get; set; }
        public double SpeedMBps { get; set; }
        public int Errors { get; set; }
        public bool PartitionFormatted { get; set; } // Whether partition was created AND formatted
    }
}