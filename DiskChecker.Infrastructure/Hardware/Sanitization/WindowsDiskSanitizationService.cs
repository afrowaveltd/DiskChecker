using System;
using System.ComponentModel;
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
    private readonly ISettingsService? _settingsService;
    private const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x70140;
    private const int BUFFER_SIZE = 64 * 1024 * 1024; // 64 MB buffer
    private const int DefaultUsbRecoveryMaxRetries = 2;
    private const int InitialRawOpenMaxRetries = 6;
    private const int InitialRawOpenDelayMs = 400;

    public WindowsDiskSanitizationService(
        ILogger<WindowsDiskSanitizationService>? logger = null,
        ISettingsService? settingsService = null)
    {
        _logger = logger;
        _settingsService = settingsService;
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
            result.ErrorDetails.AddRange(dismountResult.ErrorDetails);
            if (!dismountResult.Success)
            {
                result.ErrorMessage = dismountResult.ErrorMessage;
                return result;
            }

            var startupHandshake = await EnsureRawIoReadyAsync(devicePath, driveNumber, progress, cancellationToken);
            result.ErrorDetails.AddRange(startupHandshake.ErrorDetails);
            if (!startupHandshake.Success)
            {
                result.ErrorMessage = startupHandshake.ErrorMessage;
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
            result.ErrorDetails.AddRange(writeResult.ErrorDetails);
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

                var partitionResult = await CreateGptPartitionAsync(devicePath, cancellationToken);
                result.ErrorDetails.AddRange(partitionResult.ErrorDetails);
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
                result.ErrorDetails.AddRange(formatResult.ErrorDetails);
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

                result.Success = true;
                if (_logger != null && _logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Disk {DriveNumber} was offlined for exclusive raw I/O access.", driveNumber);
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

    private async Task<SanitizationPhaseResult> EnsureRawIoReadyAsync(
        string devicePath,
        string driveNumber,
        IProgress<SanitizationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(devicePath);
        ArgumentNullException.ThrowIfNull(driveNumber);

        var result = new SanitizationPhaseResult();
        var physicalPath = $@"\\.\PhysicalDrive{driveNumber}";

        for (var attempt = 1; attempt <= InitialRawOpenMaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new SanitizationProgress
            {
                Phase = "Inicializace raw I/O",
                ProgressPercent = 0,
                StatusDetail = $"Kontroluji dostupnost raw přístupu: pokus {attempt}/{InitialRawOpenMaxRetries}."
            });

            await RefreshDiskPropertiesAsync(physicalPath, cancellationToken);
            await Task.Delay(InitialRawOpenDelayMs * attempt, cancellationToken);

            using var handle = CreateFileExclusive(physicalPath);
            if (handle != null && !handle.IsInvalid)
            {
                result.Success = true;
                return result;
            }

            var error = Marshal.GetLastWin32Error();
            var message = $"Raw open handshake selhal pro {physicalPath}. Chyba: {error} ({GetWin32ErrorMessage(error)}).";
            result.ErrorDetails.Add(new SanitizationErrorDetail
            {
                Phase = "StartupHandshake",
                ErrorCode = $"RAW_OPEN_{error}",
                Message = "Po odpojení svazků ještě není připraven výhradní raw přístup.",
                Details = message,
                OffsetBytes = 0
            });

            if (!IsTransientStartupError(error) || attempt >= InitialRawOpenMaxRetries)
            {
                result.ErrorMessage = message;
                return result;
            }
        }

        result.ErrorMessage = $"Disk {devicePath} nebyl připraven pro raw I/O ani po opakovaných pokusech.";
        return result;
    }

    private async Task RefreshDiskPropertiesAsync(string physicalPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(physicalPath);

        await Task.Run(() =>
        {
            using var handle = Win32DiskInterop.CreateFile(
                physicalPath,
                Win32DiskInterop.GENERIC_READ,
                Win32DiskInterop.FILE_SHARE_READ | Win32DiskInterop.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Win32DiskInterop.OPEN_EXISTING,
                Win32DiskInterop.FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (handle == null || handle.IsInvalid)
            {
                return;
            }

            _ = Win32DiskInterop.DeviceIoControl(
                handle,
                IOCTL_DISK_UPDATE_PROPERTIES,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero);
        }, cancellationToken);
    }

    private static bool IsTransientStartupError(int win32Error)
    {
        return win32Error is 5 or 21 or 32 or 87 or 1117;
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
                var bytesWrittenThisChunk = await ExecuteIoWithUsbRecoveryAsync(
                    () => WriteFileAsync(handle, buffer, bytesToWrite, cancellationToken),
                    phase: "Write",
                    driveNumber,
                    bytesWritten,
                    progress,
                    result,
                    cancellationToken);
                 chunkStopwatch.Stop();
                
                if (bytesWrittenThisChunk != bytesToWrite)
                {
                    result.Errors++;
                    result.ErrorDetails.Add(new SanitizationErrorDetail
                    {
                        Phase = "Write",
                        ErrorCode = "PARTIAL_WRITE",
                        Message = "Zápis neproběhl v plné délce bloku.",
                        Details = $"Zapsáno {bytesWrittenThisChunk} z očekávaných {bytesToWrite} bajtů.",
                        OffsetBytes = bytesWritten
                    });
                     if (_logger != null && _logger.IsEnabled(LogLevel.Warning))
                     {
                         _logger.LogWarning("Write incomplete at offset {Offset}: wrote {Written} of {Expected}", 
                             bytesWritten, bytesWrittenThisChunk, bytesToWrite);
                     }
                }

                if (result.Errors >= 10)
                {
                    result.Success = false;
                    result.ErrorMessage = "Test byl ukončen: nalezeno 10 nebo více chyb. Disk je pravděpodobně vadný.";
                    return result;
                }

                bytesWritten += bytesWrittenThisChunk;
                FlushFileBuffers(handle.DangerousGetHandle());

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
            result.ErrorDetails.Add(new SanitizationErrorDetail
            {
                Phase = "Write",
                ErrorCode = ex.HResult.ToString("X", System.Globalization.CultureInfo.InvariantCulture),
                Message = ex.Message,
                Details = ex.GetType().Name,
                OffsetBytes = bytesWritten
            });
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
                var bytesReadThisChunk = await ExecuteIoWithUsbRecoveryAsync(
                    () => ReadFileAsync(handle, buffer, bytesToRead, cancellationToken),
                    phase: "Read",
                    driveNumber,
                    bytesRead,
                    progress,
                    result,
                    cancellationToken);
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
 
                 // Verify all zeros
                var nonZeroCount = 0;
                var firstNonZeroOffset = -1;
                var verifySpan = buffer.AsSpan(0, bytesReadThisChunk);
                for (var i = 0; i < verifySpan.Length; i++)
                 {
                    if (verifySpan[i] != 0)
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
            result.ErrorDetails.Add(new SanitizationErrorDetail
            {
                Phase = "Read",
                ErrorCode = ex.HResult.ToString("X", System.Globalization.CultureInfo.InvariantCulture),
                Message = ex.Message,
                Details = ex.GetType().Name,
                OffsetBytes = bytesRead
            });
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
online disk
attributes disk clear readonly
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

                // Read output and wait for exit in parallel to avoid deadlock
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                
                var output = await outputTask;
                var error = await errorTask;

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
                    // Read output and wait for exit in parallel to avoid deadlock
                    var assignOutputTask = processAssign.StandardOutput.ReadToEndAsync(cancellationToken);
                    var assignErrorTask = processAssign.StandardError.ReadToEndAsync(cancellationToken);
                    await processAssign.WaitForExitAsync(cancellationToken);
                    
                    var assignOutput = await assignOutputTask;
                    var assignError = await assignErrorTask;
                    
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

            // Read output and wait for exit in parallel to avoid deadlock
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            
            var output = await outputTask;
            var error = await errorTask;

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

            // Read output and wait for exit in parallel to avoid deadlock
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            
            var output = await outputTask;
            var error = await errorTask;

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
                    throw CreateIoException(error, "WriteFile");
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
                    throw CreateIoException(error, "ReadFile");
                }
                return (int)bytesRead;
            }
            finally
            {
                bufferHandle.Free();
            }
        }, cancellationToken);
    }

    private async Task<int> ExecuteIoWithUsbRecoveryAsync(
        Func<Task<int>> ioOperation,
        string phase,
        string driveNumber,
        long offsetBytes,
        IProgress<SanitizationProgress>? progress,
        SanitizationPhaseResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ioOperation);

        var maxRetries = await GetUsbRecoveryMaxRetriesAsync(cancellationToken);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ioOperation();
            }
            catch (IOException ex) when (IsUsbCommunicationIssue(ex) && attempt < maxRetries)
            {
                result.ErrorDetails.Add(new SanitizationErrorDetail
                {
                    Phase = phase,
                    ErrorCode = "USB_COMM_RETRY",
                    Message = "Detekován problém USB komunikace, pokouším se o reset a opakování.",
                    Details = ex.Message,
                    OffsetBytes = offsetBytes
                });

                progress?.Report(new SanitizationProgress
                {
                    Phase = "USB recovery",
                    ProgressPercent = 0,
                    StatusDetail = $"{phase} offset {offsetBytes:#,0}: pokus {attempt + 1}/{maxRetries} - {ex.Message}"
                });

                _logger?.LogWarning(ex,
                    "Transient USB communication issue during {Phase} at offset {Offset}. Attempt {Attempt}/{Max}.",
                    phase, offsetBytes, attempt + 1, maxRetries);

                await TryResetUsbCommunicationAsync(driveNumber, cancellationToken);
                await Task.Delay(300, cancellationToken);
            }
            catch (IOException ex) when (IsUsbCommunicationIssue(ex))
            {
                progress?.Report(new SanitizationProgress
                {
                    Phase = "USB recovery",
                    ProgressPercent = 0,
                    StatusDetail = $"{phase} offset {offsetBytes:#,0}: vyčerpány retry pokusy - {ex.Message}"
                });
                throw;
            }
        }
    }

    /// <summary>
    /// Attempts to recover transient USB communication by toggling disk offline/online and rescanning.
    /// </summary>
    private async Task TryResetUsbCommunicationAsync(string driveNumber, CancellationToken cancellationToken)
    {
        var diskpartScript = $"select disk {driveNumber}{Environment.NewLine}offline disk{Environment.NewLine}online disk{Environment.NewLine}rescan";
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"diskpart_usb_recover_{Guid.NewGuid():N}.txt");

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
                return;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;
            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("USB recovery diskpart output: {Output}; error: {Error}", output, error);
            }
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, "USB recovery attempt failed due to IO error.");
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogWarning(ex, "USB recovery attempt failed due to process error.");
        }
        finally
        {
            try
            {
                File.Delete(tempScriptPath);
            }
            catch (IOException)
            {
                // Ignore cleanup errors.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore cleanup errors.
            }
        }
    }

    /// <summary>
    /// Gets configured max retries for USB communication recovery.
    /// </summary>
    private async Task<int> GetUsbRecoveryMaxRetriesAsync(CancellationToken cancellationToken)
    {
        if (_settingsService == null)
        {
            return DefaultUsbRecoveryMaxRetries;
        }

        try
        {
            var configured = await _settingsService.GetUsbRecoveryRetryCountAsync();
            return Math.Clamp(configured, 0, 10);
        }
        catch (InvalidOperationException)
        {
            return DefaultUsbRecoveryMaxRetries;
        }
    }

    private static bool IsUsbCommunicationIssue(IOException ex)
    {
        var win32 = ex.HResult & 0xFFFF;
        return win32 is 23 or 31 or 64 or 1117 or 121;
    }

    private static IOException CreateIoException(int win32Error, string operation)
    {
        var message = $"{operation} selhal: {GetWin32ErrorMessage(win32Error)} (Win32: {win32Error}).";
        return new IOException(message, new Win32Exception(win32Error));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushFileBuffers(IntPtr hFile);

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
            23 => "Datová chyba (cyklická redundance - CRC)",
            31 => "Zařízení nefunguje správně",
            64 => "Síťové/jádrové spojení bylo přerušeno",
            121 => "Vypršel časový limit I/O (semaphore timeout)",
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
        public List<SanitizationErrorDetail> ErrorDetails { get; } = new();
    }
 }
