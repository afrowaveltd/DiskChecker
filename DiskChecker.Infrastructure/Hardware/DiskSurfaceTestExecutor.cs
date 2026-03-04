using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Low-level disk surface test executor with NO OS buffering.
/// Writes directly to disk sectors and verifies integrity.
/// Supports both Windows (raw device access) and Linux (direct I/O).
/// </summary>
public class DiskSurfaceTestExecutor : ISurfaceTestExecutor
{
    private readonly ISmartaProvider _smartaProvider;

    // P/Invoke constants for Windows
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

    // P/Invoke constants for Linux
    private const int O_DIRECT = 0x4000;
    private const int O_SYNC = 0x101000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateFileW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        IntPtr hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(
        IntPtr hFile,
        long liDistanceToMove,
        out long lpNewFilePointer,
        uint dwMoveMethod);

    // Linux P/Invoke
#pragma warning disable CA2101 // Specify marshaling for P/Invoke string arguments
    [DllImport("libc", SetLastError = true)]
    private static extern int open(
        [MarshalAs(UnmanagedType.LPStr)] string pathname,
        int flags);
#pragma warning restore CA2101

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int write(int fd, byte[] buf, int count);

    [DllImport("libc", SetLastError = true)]
    private static extern int read(int fd, byte[] buf, int count);

    [DllImport("libc", SetLastError = true)]
    private static extern long lseek(int fd, long offset, int whence);

    public DiskSurfaceTestExecutor(ISmartaProvider smartaProvider)
    {
        ArgumentNullException.ThrowIfNull(smartaProvider);
        _smartaProvider = smartaProvider;
    }

    public async Task<SurfaceTestResult> ExecuteAsync(
        SurfaceTestRequest request,
        IProgress<SurfaceTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Drive);

        var result = new SurfaceTestResult
        {
            TestId = Guid.NewGuid().ToString(),
            Profile = request.Profile,
            Operation = request.Operation,
            StartedAtUtc = DateTime.UtcNow,
            SecureErasePerformed = false
        };

        // Determine platform
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        if (!isWindows && !isLinux)
        {
            result.ErrorCount = 1;
            result.Notes = "CHYBA: Podporovány jsou pouze Windows a Linux.";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
        }

        // Get disk capacity
        long diskCapacity = request.Drive.TotalSize;
        long maxBytesToTest = request.MaxBytesToTest ?? diskCapacity;

        if (maxBytesToTest <= 0 || diskCapacity <= 0)
        {
            result.ErrorCount = 1;
            result.Notes = "CHYBA: Nelze určit kapacitu disku.";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
        }

        // Collect SMART data
        try
        {
            var smartData = await _smartaProvider.GetSmartaDataAsync(request.Drive.Path, cancellationToken);
            if (smartData != null)
            {
                result.DriveModel = smartData.DeviceModel ?? request.Drive.Name;
                result.DriveSerialNumber = smartData.SerialNumber;
                result.DriveManufacturer = ExtractManufacturer(smartData.DeviceModel);
                result.DriveTotalBytes = diskCapacity;
                result.PowerOnHours = smartData.PowerOnHours > 0 ? smartData.PowerOnHours : null;
                result.CurrentTemperatureCelsius = smartData.Temperature > 0 ? (int)smartData.Temperature : null;
                result.ReallocatedSectors = smartData.ReallocatedSectorCount > 0 ? smartData.ReallocatedSectorCount : null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Warning: Could not read SMART data: {ex.Message}");
        }

        // Perform low-level test
        if (isWindows)
        {
            return await ExecuteWindowsDiskTestAsync(request, result, maxBytesToTest, progress, cancellationToken);
        }
        else
        {
            return await ExecuteLinuxDiskTestAsync(request, result, maxBytesToTest, progress, cancellationToken);
        }
    }

    /// <summary>
    /// Windows low-level disk test using raw device access.
    /// </summary>
    private async Task<SurfaceTestResult> ExecuteWindowsDiskTestAsync(
        SurfaceTestRequest request,
        SurfaceTestResult result,
        long maxBytesToTest,
        IProgress<SurfaceTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        const int BUFFER_SIZE = 10 * 1024 * 1024; // 10 MB buffer
        const int MaxMillisecondsBetweenProgressReports = 500;

        var buffer = new byte[BUFFER_SIZE];
        Array.Fill(buffer, (byte)0xA5); // Fill with pattern

        var diskPath = request.Drive.Path;
        IntPtr diskHandle = IntPtr.Zero;

        try
        {
            // Open disk with NO buffering
            diskHandle = CreateFileW(
                diskPath,
                GENERIC_READ | GENERIC_WRITE,
                0, // Exclusive access
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH,
                IntPtr.Zero);

            if (diskHandle == new IntPtr(-1))
            {
                result.ErrorCount = 1;
                result.Notes = $"CHYBA: Nelze otevřít disk {diskPath}. Jsou potřebná administrátorská práva.";
                result.CompletedAtUtc = DateTime.UtcNow;
                return result;
            }

            result.Notes = "Fáze 1: Přímý zápis na disk bez OS cachování";
            var totalStopwatch = Stopwatch.StartNew();
            var sampleStopwatch = Stopwatch.StartNew();
            var lastProgressReport = DateTime.UtcNow;

            long bytesWritten = 0;
            long bytesRead = 0;
            var samples = new List<SurfaceTestSample>();
            double peak = 0;
            double min = double.MaxValue;

            // Phase 1: Write
            while (bytesWritten < maxBytesToTest && !cancellationToken.IsCancellationRequested)
            {
                var toWrite = (uint)Math.Min(BUFFER_SIZE, maxBytesToTest - bytesWritten);

                if (!WriteFile(diskHandle, buffer, toWrite, out var written, IntPtr.Zero))
                {
                    result.ErrorCount++;
                    result.Notes = $"Chyba zápisu: {Marshal.GetLastWin32Error()}";
                    if (result.ErrorCount > 5)
                        break;
                    continue;
                }

                bytesWritten += written;

                // Report progress every 500ms or when significant amount written
                if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= MaxMillisecondsBetweenProgressReports)
                {
                    ReportProgress(
                        bytesWritten, maxBytesToTest, sampleStopwatch, samples,
                        progress, result, ref peak, ref min);

                    lastProgressReport = DateTime.UtcNow;
                    sampleStopwatch.Restart();
                }
            }

            // Phase 2: Read verification
            result.Notes = "Fáze 2: Verifikace čtením bez OS cachování";
            SetFilePointerEx(diskHandle, 0, out _, 0); // Seek to beginning

            sampleStopwatch.Restart();
            lastProgressReport = DateTime.UtcNow;

            while (bytesRead < maxBytesToTest && !cancellationToken.IsCancellationRequested)
            {
                var toRead = (uint)Math.Min(BUFFER_SIZE, maxBytesToTest - bytesRead);

                if (!ReadFile(diskHandle, buffer, toRead, out var read, IntPtr.Zero))
                {
                    result.ErrorCount++;
                    result.Notes = $"Chyba čtení: {Marshal.GetLastWin32Error()}";
                    if (result.ErrorCount > 5)
                        break;
                    continue;
                }

                // Verify pattern
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != 0xA5)
                    {
                        result.ErrorCount++;
                        if (result.ErrorCount > 100)
                        {
                            result.Notes = "Příliš mnoho chyb verifikace - disk je vadný!";
                            goto cleanup;
                        }
                    }
                }

                bytesRead += read;

                // Report progress
                if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= MaxMillisecondsBetweenProgressReports)
                {
                    ReportProgress(
                        bytesRead, maxBytesToTest, sampleStopwatch, samples,
                        progress, result, ref peak, ref min, bytesProcessedPreviousPhases: bytesWritten);

                    lastProgressReport = DateTime.UtcNow;
                    sampleStopwatch.Restart();
                }
            }

            cleanup:
            totalStopwatch.Stop();
            result.CompletedAtUtc = DateTime.UtcNow;
            result.TotalBytesTested = bytesRead;
            result.Samples = samples;
            result.PeakSpeedMbps = peak;
            result.MinSpeedMbps = min == double.MaxValue ? 0 : min;

            if (totalStopwatch.Elapsed.TotalSeconds > 0 && bytesRead > 0)
            {
                result.AverageSpeedMbps = bytesRead / (1024.0 * 1024.0) / totalStopwatch.Elapsed.TotalSeconds;
            }

            result.Notes = result.ErrorCount == 0
                ? $"✓ Test úspěšný: Zapsáno a ověřeno {FormatBytes(bytesRead)}"
                : $"⚠ Test s chybami: {result.ErrorCount} chyb(y) zjištěno";
        }
        finally
        {
            if (diskHandle != IntPtr.Zero && diskHandle != new IntPtr(-1))
            {
                CloseHandle(diskHandle);
            }
        }

        return result;
    }

    /// <summary>
    /// Linux low-level disk test using direct I/O.
    /// </summary>
    private async Task<SurfaceTestResult> ExecuteLinuxDiskTestAsync(
        SurfaceTestRequest request,
        SurfaceTestResult result,
        long maxBytesToTest,
        IProgress<SurfaceTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        const int BUFFER_SIZE = 10 * 1024 * 1024;
        const int MaxMillisecondsBetweenProgressReports = 500;

        // Allocate aligned buffer for O_DIRECT
        var buffer = Marshal.AllocHGlobal(BUFFER_SIZE);
        var managedBuffer = new byte[BUFFER_SIZE];
        Array.Fill(managedBuffer, (byte)0xA5);
        Marshal.Copy(managedBuffer, 0, buffer, BUFFER_SIZE);

        int diskHandle = -1;

        try
        {
            // Open device with O_DIRECT (no OS caching)
            diskHandle = open(request.Drive.Path, O_DIRECT | O_SYNC | 0x0002); // O_RDWR

            if (diskHandle < 0)
            {
                result.ErrorCount = 1;
                result.Notes = $"CHYBA: Nelze otevřít disk {request.Drive.Path}. Jsou potřebná administrátorská práva.";
                result.CompletedAtUtc = DateTime.UtcNow;
                return result;
            }

            result.Notes = "Fáze 1: Přímý zápis na disk bez OS cachování";
            var totalStopwatch = Stopwatch.StartNew();
            var sampleStopwatch = Stopwatch.StartNew();
            var lastProgressReport = DateTime.UtcNow;

            long bytesWritten = 0;
            long bytesRead = 0;
            var samples = new List<SurfaceTestSample>();
            double peak = 0;
            double min = double.MaxValue;

            // Phase 1: Write
            while (bytesWritten < maxBytesToTest && !cancellationToken.IsCancellationRequested)
            {
                int toWrite = (int)Math.Min(BUFFER_SIZE, maxBytesToTest - bytesWritten);
                int written = write(diskHandle, managedBuffer, toWrite);

                if (written < 0)
                {
                    result.ErrorCount++;
                    result.Notes = $"Chyba zápisu: errno {Marshal.GetLastWin32Error()}";
                    if (result.ErrorCount > 5)
                        break;
                    continue;
                }

                bytesWritten += written;

                if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= MaxMillisecondsBetweenProgressReports)
                {
                    ReportProgress(
                        bytesWritten, maxBytesToTest, sampleStopwatch, samples,
                        progress, result, ref peak, ref min);

                    lastProgressReport = DateTime.UtcNow;
                    sampleStopwatch.Restart();
                }
            }

            // Phase 2: Read verification
            result.Notes = "Fáze 2: Verifikace čtením bez OS cachování";
            lseek(diskHandle, 0, 0); // SEEK_SET

            sampleStopwatch.Restart();
            lastProgressReport = DateTime.UtcNow;

            while (bytesRead < maxBytesToTest && !cancellationToken.IsCancellationRequested)
            {
                int toRead = (int)Math.Min(BUFFER_SIZE, maxBytesToTest - bytesRead);
                int readBytes = read(diskHandle, managedBuffer, toRead);

                if (readBytes < 0)
                {
                    result.ErrorCount++;
                    result.Notes = $"Chyba čtení: errno {Marshal.GetLastWin32Error()}";
                    if (result.ErrorCount > 5)
                        break;
                    continue;
                }

                if (readBytes == 0)
                    break;

                // Verify pattern
                for (int i = 0; i < readBytes; i++)
                {
                    if (managedBuffer[i] != 0xA5)
                    {
                        result.ErrorCount++;
                        if (result.ErrorCount > 100)
                        {
                            result.Notes = "Příliš mnoho chyb verifikace - disk je vadný!";
                            goto cleanup;
                        }
                    }
                }

                bytesRead += readBytes;

                if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= MaxMillisecondsBetweenProgressReports)
                {
                    ReportProgress(
                        bytesRead, maxBytesToTest, sampleStopwatch, samples,
                        progress, result, ref peak, ref min, bytesProcessedPreviousPhases: bytesWritten);

                    lastProgressReport = DateTime.UtcNow;
                    sampleStopwatch.Restart();
                }
            }

            cleanup:
            totalStopwatch.Stop();
            result.CompletedAtUtc = DateTime.UtcNow;
            result.TotalBytesTested = bytesRead;
            result.Samples = samples;
            result.PeakSpeedMbps = peak;
            result.MinSpeedMbps = min == double.MaxValue ? 0 : min;

            if (totalStopwatch.Elapsed.TotalSeconds > 0 && bytesRead > 0)
            {
                result.AverageSpeedMbps = bytesRead / (1024.0 * 1024.0) / totalStopwatch.Elapsed.TotalSeconds;
            }

            result.Notes = result.ErrorCount == 0
                ? $"✓ Test úspěšný: Zapsáno a ověřeno {FormatBytes(bytesRead)}"
                : $"⚠ Test s chybami: {result.ErrorCount} chyb(y) zjištěno";
        }
        finally
        {
            if (diskHandle >= 0)
            {
                var closeResult = close(diskHandle);
                if (closeResult != 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: close() returned {closeResult}");
                }
            }
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return result;
    }

    private static void ReportProgress(
        long bytesProcessedThisPhase,
        long totalBytesThisPhase,
        Stopwatch sampleStopwatch,
        List<SurfaceTestSample> samples,
        IProgress<SurfaceTestProgress>? progress,
        SurfaceTestResult result,
        ref double peak,
        ref double min,
        long? bytesProcessedPreviousPhases = null)
    {
        if (sampleStopwatch.Elapsed.TotalSeconds <= 0)
            return;

        var throughput = bytesProcessedThisPhase / (1024d * 1024d) / sampleStopwatch.Elapsed.TotalSeconds;

        samples.Add(new SurfaceTestSample
        {
            OffsetBytes = bytesProcessedThisPhase,
            BlockSizeBytes = 4096,
            ThroughputMbps = Math.Round(throughput, 2),
            TimestampUtc = DateTime.UtcNow,
            ErrorCount = 0
        });

        peak = Math.Max(peak, throughput);
        min = Math.Min(min, throughput);

        bool isVerifyPhase = bytesProcessedPreviousPhases.HasValue;
        double phasePercent = totalBytesThisPhase == 0
            ? 0
            : Math.Min(100, bytesProcessedThisPhase * 100d / totalBytesThisPhase);

        // WRITE phase: 0-50 %, VERIFY phase: 50-100 %
        double globalPercentComplete = isVerifyPhase
            ? 50 + (phasePercent / 2.0)
            : (phasePercent / 2.0);

        // Reportujeme vždy objem zpracovaný v AKTUÁLNÍ fázi,
        // aby UI mohlo zobrazit zvlášť zapsaná a zvlášť ověřená data.
        long reportedBytes = bytesProcessedThisPhase;

        progress?.Report(new SurfaceTestProgress
        {
            TestId = Guid.Parse(result.TestId),
            BytesProcessed = reportedBytes,
            PercentComplete = globalPercentComplete,
            CurrentThroughputMbps = Math.Round(throughput, 2),
            TimestampUtc = DateTime.UtcNow
        });
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double b = bytes;
        while (b >= 1024 && i < sizes.Length - 1)
        {
            b /= 1024;
            i++;
        }
        return $"{b:F1} {sizes[i]}";
    }

    private static string? ExtractManufacturer(string? modelNumber)
    {
        if (string.IsNullOrEmpty(modelNumber))
            return null;

        var upper = modelNumber.ToUpperInvariant();

        return upper switch
        {
            var x when x.StartsWith("ST", StringComparison.Ordinal) => "Seagate",
            var x when x.StartsWith("WD", StringComparison.Ordinal) => "Western Digital",
            var x when x.StartsWith("SAMSUNG", StringComparison.Ordinal) => "Samsung",
            var x when x.StartsWith("INTEL", StringComparison.Ordinal) => "Intel",
            var x when x.StartsWith("TOSHIBA", StringComparison.Ordinal) => "Toshiba",
            var x when x.StartsWith("KINGSTON", StringComparison.Ordinal) => "Kingston",
            var x when x.StartsWith("CRUCIAL", StringComparison.Ordinal) => "Crucial",
            var x when x.StartsWith("SK HYNIX", StringComparison.Ordinal) => "SK Hynix",
            var x when x.StartsWith("HITACHI", StringComparison.Ordinal) => "Hitachi",
            var x when x.StartsWith("MAXTOR", StringComparison.Ordinal) => "Maxtor",
            var x when x.StartsWith("ADATA", StringComparison.Ordinal) => "ADATA",
            var x when x.StartsWith("SANDISK", StringComparison.Ordinal) => "SanDisk",
            _ => null
        };
    }
}
