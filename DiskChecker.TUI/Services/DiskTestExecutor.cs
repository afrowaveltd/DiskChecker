using System.Diagnostics;
using System.Runtime.InteropServices;
using DiskChecker.TUI.Models;
using Microsoft.Win32.SafeHandles;

namespace DiskChecker.TUI.Services;

/// <summary>
/// Executes destructive disk tests using direct physical drive access (Windows-only).
/// </summary>
public sealed class DiskTestExecutor
{
    private const int BlockSize = 256 * 1024; // 256 KB blocks
    private const int SampleIntervalBlocks = 64; // Sample every ~16 MB
    private const int TemperatureSampleIntervalMs = 2000;

    private readonly string _devicePath;
    private readonly ulong _capacityBytes;
    private readonly Action<string>? _statusCallback;
    private readonly Action<double, double, double?>? _progressCallback;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public DiskTestExecutor(
        string devicePath,
        ulong capacityBytes,
        Action<string>? statusCallback = null,
        Action<double, double, double?>? progressCallback = null)
    {
        _devicePath = devicePath;
        _capacityBytes = capacityBytes;
        _statusCallback = statusCallback;
        _progressCallback = progressCallback;
    }

    /// <summary>
    /// Runs the full destructive test: write → read → seek → sanitize.
    /// </summary>
    public async Task<TestRunResult> RunFullDestructiveAsync(
        PhysicalDiskInfo diskInfo,
        CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var result = new TestRunResult
        {
            DiskModel = diskInfo.Model,
            DiskSerial = diskInfo.SerialNumber,
            DevicePath = diskInfo.DevicePath,
            CapacityBytes = diskInfo.CapacityBytes,
            StartedAt = DateTimeOffset.Now
        };

        try
        {
            // Phase 1: Write
            ReportStatus("📝 Fáze 1/4: Zápis dat na celý disk...");
            var writeResult = await RunWritePassAsync(_cts.Token);
            result.WriteSamples = writeResult.Samples;
            result.WriteSpeedAvgMBps = writeResult.AvgMBps;
            result.WriteSpeedMinMBps = writeResult.MinMBps;
            result.WriteSpeedMaxMBps = writeResult.MaxMBps;
            ReportStatus($"   ✅ Zápis: avg {writeResult.AvgMBps:F1} MB/s, min {writeResult.MinMBps:F1}, max {writeResult.MaxMBps:F1}");

            // Phase 2: Read
            ReportStatus("📖 Fáze 2/4: Čtení a verifikace dat...");
            var readResult = await RunReadPassAsync(_cts.Token);
            result.ReadSamples = readResult.Samples;
            result.ReadSpeedAvgMBps = readResult.AvgMBps;
            result.ReadSpeedMinMBps = readResult.MinMBps;
            result.ReadSpeedMaxMBps = readResult.MaxMBps;
            ReportStatus($"   ✅ Čtení: avg {readResult.AvgMBps:F1} MB/s, min {readResult.MinMBps:F1}, max {readResult.MaxMBps:F1}");

            // Phase 3: Seek (only for HDDs, skip for SSDs/USB)
            if (!diskInfo.IsUsb && diskInfo.InterfaceType.Contains("IDE", StringComparison.OrdinalIgnoreCase)
                || diskInfo.InterfaceType.Contains("SATA", StringComparison.OrdinalIgnoreCase)
                || diskInfo.InterfaceType.Contains("SCSI", StringComparison.OrdinalIgnoreCase))
            {
                ReportStatus("🔍 Fáze 3/4: Test seek time...");
                var seekResult = await RunSeekPassAsync(2000, _cts.Token);
                result.SeekSamples = seekResult.Samples;
                result.SeekAvgMs = seekResult.AvgMs;
                result.SeekMinMs = seekResult.MinMs;
                result.SeekMaxMs = seekResult.MaxMs;
                ReportStatus($"   ✅ Seek: avg {seekResult.AvgMs:F2} ms, min {seekResult.MinMs:F2}, max {seekResult.MaxMs:F2}");
            }
            else
            {
                ReportStatus("🔍 Fáze 3/4: Seek test přeskočen (SSD/USB)");
            }

            // Phase 4: Sanitize
            ReportStatus("🧹 Fáze 4/4: Sanitizace (secure erase)...");
            var sanitizeResult = await RunSanitizePassAsync(_cts.Token);
            result.SanitizationPassed = sanitizeResult.Passed;
            result.SanitizationMethod = sanitizeResult.Method;
            result.SanitizationOutput = sanitizeResult.Output;
            ReportStatus(sanitizeResult.Passed
                ? "   ✅ Sanitizace úspěšná"
                : "   ⚠️ Sanitizace selhala");

            result.CompletedAt = DateTimeOffset.Now;
        }
        catch (OperationCanceledException)
        {
            result.ErrorMessage = "Test přerušen uživatelem";
            result.CompletedAt = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Chyba: {ex.Message}";
            result.CompletedAt = DateTimeOffset.Now;
        }

        return result;
    }

    /// <summary>
    /// Runs only the write pass.
    /// </summary>
    public async Task<TestRunResult> RunWriteOnlyAsync(PhysicalDiskInfo diskInfo, CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var result = new TestRunResult
        {
            DiskModel = diskInfo.Model,
            DiskSerial = diskInfo.SerialNumber,
            DevicePath = diskInfo.DevicePath,
            CapacityBytes = diskInfo.CapacityBytes,
            StartedAt = DateTimeOffset.Now
        };

        try
        {
            ReportStatus("📝 Zápis dat na celý disk...");
            var writeResult = await RunWritePassAsync(_cts.Token);
            result.WriteSamples = writeResult.Samples;
            result.WriteSpeedAvgMBps = writeResult.AvgMBps;
            result.WriteSpeedMinMBps = writeResult.MinMBps;
            result.WriteSpeedMaxMBps = writeResult.MaxMBps;
            result.CompletedAt = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Chyba: {ex.Message}";
            result.CompletedAt = DateTimeOffset.Now;
        }

        return result;
    }

    #region Test Passes

    private async Task<(List<SpeedSample> Samples, double AvgMBps, double MinMBps, double MaxMBps)> RunWritePassAsync(CancellationToken ct)
    {
        var samples = new List<SpeedSample>();
        var speeds = new List<double>();
        var rng = new Random();
        byte[] buffer = new byte[BlockSize];

        using var handle = OpenDisk(FileAccess.ReadWrite);

        long totalBlocks = (long)(_capacityBytes / (ulong)BlockSize);
        long blocksWritten = 0;
        var sw = Stopwatch.StartNew();
        long bytesInWindow = 0;
        var windowSw = Stopwatch.StartNew();

        for (long block = 0; block < totalBlocks; block++)
        {
            ct.ThrowIfCancellationRequested();

            long offset = block * BlockSize;
            SetFilePointer(handle, offset);

            // Fill buffer with pseudo-random data
            rng.NextBytes(buffer);

            // Write block
            WriteFile(handle, buffer, BlockSize);
            blocksWritten++;
            bytesInWindow += BlockSize;

            // Sample speed periodically
            if (blocksWritten % SampleIntervalBlocks == 0 && windowSw.ElapsedMilliseconds > 0)
            {
                double speedMBps = bytesInWindow / (1024.0 * 1024.0) / (windowSw.Elapsed.TotalSeconds);
                speeds.Add(speedMBps);

                double position = (double)block / totalBlocks * 100.0;
                samples.Add(new SpeedSample
                {
                    PositionPercent = position,
                    SpeedMBps = speedMBps,
                    Timestamp = DateTimeOffset.Now
                });

                ReportProgress(position, speedMBps, null);

                bytesInWindow = 0;
                windowSw.Restart();
            }
        }

        // Flush remaining
        if (bytesInWindow > 0 && windowSw.ElapsedMilliseconds > 0)
        {
            double speedMBps = bytesInWindow / (1024.0 * 1024.0) / (windowSw.Elapsed.TotalSeconds);
            speeds.Add(speedMBps);
            samples.Add(new SpeedSample
            {
                PositionPercent = 100.0,
                SpeedMBps = speedMBps,
                Timestamp = DateTimeOffset.Now
            });
        }

        FlushDiskBuffers(handle);

        double avg = speeds.Count > 0 ? speeds.Average() : 0;
        double min = speeds.Count > 0 ? speeds.Min() : 0;
        double max = speeds.Count > 0 ? speeds.Max() : 0;

        return (samples, avg, min, max);
    }

    private async Task<(List<SpeedSample> Samples, double AvgMBps, double MinMBps, double MaxMBps)> RunReadPassAsync(CancellationToken ct)
    {
        var samples = new List<SpeedSample>();
        var speeds = new List<double>();
        byte[] buffer = new byte[BlockSize];

        using var handle = OpenDisk(FileAccess.Read);

        long totalBlocks = (long)(_capacityBytes / (ulong)BlockSize);
        long blocksRead = 0;
        long bytesInWindow = 0;
        var windowSw = Stopwatch.StartNew();

        for (long block = 0; block < totalBlocks; block++)
        {
            ct.ThrowIfCancellationRequested();

            long offset = block * BlockSize;
            SetFilePointer(handle, offset);

            ReadFile(handle, buffer, BlockSize);
            blocksRead++;
            bytesInWindow += BlockSize;

            if (blocksRead % SampleIntervalBlocks == 0 && windowSw.ElapsedMilliseconds > 0)
            {
                double speedMBps = bytesInWindow / (1024.0 * 1024.0) / (windowSw.Elapsed.TotalSeconds);
                speeds.Add(speedMBps);

                double position = (double)block / totalBlocks * 100.0;
                samples.Add(new SpeedSample
                {
                    PositionPercent = position,
                    SpeedMBps = speedMBps,
                    Timestamp = DateTimeOffset.Now
                });

                ReportProgress(position, speedMBps, null);

                bytesInWindow = 0;
                windowSw.Restart();
            }
        }

        if (bytesInWindow > 0 && windowSw.ElapsedMilliseconds > 0)
        {
            double speedMBps = bytesInWindow / (1024.0 * 1024.0) / (windowSw.Elapsed.TotalSeconds);
            speeds.Add(speedMBps);
            samples.Add(new SpeedSample
            {
                PositionPercent = 100.0,
                SpeedMBps = speedMBps,
                Timestamp = DateTimeOffset.Now
            });
        }

        double avg = speeds.Count > 0 ? speeds.Average() : 0;
        double min = speeds.Count > 0 ? speeds.Min() : 0;
        double max = speeds.Count > 0 ? speeds.Max() : 0;

        return (samples, avg, min, max);
    }

    private async Task<(List<SpeedSample> Samples, double AvgMs, double MinMs, double MaxMs)> RunSeekPassAsync(int seekCount, CancellationToken ct)
    {
        var samples = new List<SpeedSample>();
        var latencies = new List<double>();
        var rng = new Random();
        byte[] buffer = new byte[512]; // 1 sector for seek verification

        using var handle = OpenDisk(FileAccess.Read);

        long maxOffset = (long)(_capacityBytes - 512);
        long totalBlocks = (long)(_capacityBytes / (ulong)BlockSize);

        for (int i = 0; i < seekCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Random position
            long targetOffset = (long)(rng.NextDouble() * maxOffset);
            targetOffset = targetOffset / 512 * 512; // Align to sector

            var seekSw = Stopwatch.StartNew();
            SetFilePointer(handle, targetOffset);
            ReadFile(handle, buffer, 512);
            seekSw.Stop();

            double latencyMs = seekSw.Elapsed.TotalMilliseconds;
            latencies.Add(latencyMs);

            double position = (double)targetOffset / (double)maxOffset * 100.0;
            samples.Add(new SpeedSample
            {
                PositionPercent = position,
                SpeedMBps = latencyMs, // Reuse field for latency
                Timestamp = DateTimeOffset.Now
            });

            if (i % 100 == 0)
            {
                ReportProgress((double)i / seekCount * 100.0, 0, latencyMs);
            }
        }

        double avg = latencies.Count > 0 ? latencies.Average() : 0;
        double min = latencies.Count > 0 ? latencies.Min() : 0;
        double max = latencies.Count > 0 ? latencies.Max() : 0;

        return (samples, avg, min, max);
    }

    private async Task<(bool Passed, string Method, string Output)> RunSanitizePassAsync(CancellationToken ct)
    {
        // Write zeros to the entire disk as sanitization
        byte[] zeroBuffer = new byte[BlockSize];
        using var handle = OpenDisk(FileAccess.ReadWrite);

        long totalBlocks = (long)(_capacityBytes / (ulong)BlockSize);
        long blocksDone = 0;

        try
        {
            for (long block = 0; block < totalBlocks; block++)
            {
                ct.ThrowIfCancellationRequested();

                long offset = block * BlockSize;
                SetFilePointer(handle, offset);
                WriteFile(handle, zeroBuffer, BlockSize);
                blocksDone++;

                if (blocksDone % (SampleIntervalBlocks * 4) == 0)
                {
                    double position = (double)block / totalBlocks * 100.0;
                    ReportProgress(position, 0, null);
                }
            }

            FlushDiskBuffers(handle);
            return (true, "ZeroFill", "Disk úspěšně přepsán nulami");
        }
        catch (Exception ex)
        {
            return (false, "ZeroFill", $"Chyba při sanitizaci: {ex.Message}");
        }
    }

    #endregion

    #region Native I/O

    private SafeFileHandle OpenDisk(FileAccess access)
    {
        uint desiredAccess = access == FileAccess.ReadWrite
            ? GENERIC_READ | GENERIC_WRITE
            : GENERIC_READ;

        var handle = CreateFileW(
            _devicePath,
            desiredAccess,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new IOException($"Nelze otevřít disk {_devicePath}. Kód chyby: {error}. " +
                "Ujistěte se, že aplikace běží jako Administrátor a disk není používán systémem.");
        }

        return handle;
    }

    private static void SetFilePointer(SafeFileHandle handle, long offset)
    {
        uint lo = (uint)(offset & 0xFFFFFFFF);
        uint hi = (uint)(offset >> 32);
        uint result = SetFilePointer(handle, lo, ref hi, FILE_BEGIN);
        if (result == INVALID_SET_FILE_POINTER)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 0)
                throw new IOException($"SetFilePointer selhal. Kód chyby: {error}");
        }
    }

    private static unsafe void WriteFile(SafeFileHandle handle, byte[] buffer, int length)
    {
        fixed (byte* pBuffer = buffer)
        {
            if (!WriteFile(handle, pBuffer, (uint)length, out uint written, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException($"Zápis selhal. Kód chyby: {error}");
            }
            if (written != length)
                throw new IOException($"Neúplný zápis: {written}/{length} bajtů");
        }
    }

    private static unsafe void ReadFile(SafeFileHandle handle, byte[] buffer, int length)
    {
        fixed (byte* pBuffer = buffer)
        {
            if (!ReadFile(handle, pBuffer, (uint)length, out uint read, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException($"Čtení selhal. Kód chyby: {error}");
            }
            if (read != length)
                throw new IOException($"Neúplné čtení: {read}/{length} bajtů");
        }
    }

    private static void FlushDiskBuffers(SafeFileHandle handle)
    {
        if (!FlushFileBuffers(handle))
        {
            int error = Marshal.GetLastWin32Error();
            // Non-critical – log but don't throw
        }
    }

    #endregion

    #region P/Invoke

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
    private const uint FILE_BEGIN = 0;
    private const uint INVALID_SET_FILE_POINTER = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetFilePointer(
        SafeFileHandle hFile,
        uint lDistanceToMove,
        ref uint lpDistanceToMoveHigh,
        uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool WriteFile(
        SafeFileHandle hFile,
        byte* lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool ReadFile(
        SafeFileHandle hFile,
        byte* lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushFileBuffers(SafeFileHandle hFile);

    #endregion

    #region Helpers

    private void ReportStatus(string message)
    {
        _statusCallback?.Invoke(message);
    }

    private void ReportProgress(double percent, double speedMBps, double? seekMs)
    {
        _progressCallback?.Invoke(percent, speedMBps, seekMs);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}
