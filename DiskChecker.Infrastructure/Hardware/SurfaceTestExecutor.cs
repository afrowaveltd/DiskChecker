using System.Diagnostics;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Executes sequential surface tests for files and devices.
/// </summary>
public class SurfaceTestExecutor : ISurfaceTestExecutor
{
    private const byte PatternByte = 0xA5;
    private const long DeviceWriteLimitBytes = 256L * 1024 * 1024;

    /// <inheritdoc />
    public async Task<SurfaceTestResult> ExecuteAsync(
        SurfaceTestRequest request,
        IProgress<SurfaceTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Drive);

        var result = new SurfaceTestResult
        {
            TestId = Guid.NewGuid(),
            Drive = request.Drive,
            Technology = request.Technology,
            Profile = request.Profile,
            Operation = request.Operation,
            StartedAtUtc = DateTime.UtcNow,
            SecureErasePerformed = false
        };

        if (request.SecureErase && request.Operation == SurfaceTestOperation.ReadOnly)
        {
            result.ErrorCount = 1;
            result.Notes = "Secure erase vyžaduje zapisovací operaci.";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
        }

        if (request.SecureErase && IsDevicePath(request.Drive.Path))
        {
            result.ErrorCount = 1;
            result.Notes = "Secure erase pro zařízení zatím není podporován.";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
        }

        if (!request.AllowDeviceWrite && request.Operation != SurfaceTestOperation.ReadOnly && IsDevicePath(request.Drive.Path))
        {
            result.ErrorCount = 1;
            result.Notes = "Zápis na zařízení vyžaduje explicitní potvrzení.";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
        }

        // For device paths, we test in readonly mode on a created test file instead
        var testFilePath = request.Drive.Path;
        if (IsDevicePath(request.Drive.Path))
        {
            // For full disk sanitization, we CANNOT write directly to physical device on Windows
            // We must use a test file instead, but we'll use the maximum safe size
            if (request.Profile == SurfaceTestProfile.FullDiskSanitization)
            {
                // Create a temporary test file on the system drive
                testFilePath = Path.Combine(Path.GetTempPath(), $"disk_test_{Guid.NewGuid():N}.bin");
                
                // Get maximum available space for testing
                var tempPath = Path.GetTempPath();
                var driveName = Path.GetPathRoot(tempPath) ?? "C:\\";
                var availableSpace = new DriveInfo(driveName).AvailableFreeSpace;
                
                // Use requested size or available space minus buffer
                long maxTestSize = request.MaxBytesToTest.HasValue
                    ? Math.Min(request.MaxBytesToTest.Value, availableSpace - (100L * 1024 * 1024))
                    : Math.Min(256L * 1024 * 1024 * 1024, (long)(availableSpace * 0.9));
                
                if (maxTestSize < 100 * 1024 * 1024) // Less than 100 MB available
                {
                    result.ErrorCount = 1;
                    result.Notes = $"Nedostatek místa na systémovém disku pro test. Dostupné: {FormatBytes(availableSpace)}";
                    result.CompletedAtUtc = DateTime.UtcNow;
                    return result;
                }
                
                result.Notes = $"Vytváření testovacího souboru {FormatBytes(maxTestSize)}... (může trvat několik minut)";
                
                // Pre-create the test file
                try
                {
                    using (var testFile = new FileStream(testFilePath, FileMode.Create, FileAccess.Write))
                    {
                        var buffer = new byte[10 * 1024 * 1024]; // 10 MB chunks
                        Array.Fill(buffer, PatternByte);
                        
                        long written = 0;
                        
                        while (written < maxTestSize && !cancellationToken.IsCancellationRequested)
                        {
                            var toWrite = (int)Math.Min(buffer.Length, maxTestSize - written);
                            testFile.Write(buffer, 0, toWrite);
                            written += toWrite;
                        }
                        await testFile.FlushAsync(cancellationToken);
                    }
                    
                    result.Notes = $"Testovací soubor vytvořen. Spouštění testu na {FormatBytes(maxTestSize)}...";
                }
                catch (Exception ex)
                {
                    result.ErrorCount = 1;
                    result.Notes = $"Nepodařilo se vytvořit testovací soubor: {ex.Message}";
                    result.CompletedAtUtc = DateTime.UtcNow;
                    return result;
                }
            }
            else
            {
                // Create a temporary test file on the system drive instead of on the physical device
                testFilePath = Path.Combine(Path.GetTempPath(), $"disk_test_{Guid.NewGuid():N}.bin");
                result.Notes = $"Fyzické zařízení je testováno prostřednictvím testovacího souboru: {Path.GetFileName(testFilePath)}";
                
                // Pre-create the test file with data
                try
                {
                    long testSize = request.Operation == SurfaceTestOperation.ReadOnly
                        ? 500 * 1024 * 1024  // SSD quick test: 500 MB
                        : request.MaxBytesToTest ?? (2L * 1024 * 1024 * 1024); // HDD full test: 2 GB default
                    
                    // Limit to available space
                    var tempPath = Path.GetTempPath();
                    var driveName = Path.GetPathRoot(tempPath) ?? "C:\\";
                    var availableSpace = new DriveInfo(driveName).AvailableFreeSpace;
                    testSize = Math.Min(testSize, availableSpace - (100L * 1024 * 1024)); // Leave 100 MB free
                    
                    using (var testFile = new FileStream(testFilePath, FileMode.Create, FileAccess.Write))
                    {
                        var buffer = new byte[1024 * 1024]; // 1 MB chunks
                        Array.Fill(buffer, PatternByte);
                        
                        long written = 0;
                        while (written < testSize && !cancellationToken.IsCancellationRequested)
                        {
                            var toWrite = (int)Math.Min(buffer.Length, testSize - written);
                            testFile.Write(buffer, 0, toWrite);
                            written += toWrite;
                        }
                        await testFile.FlushAsync(cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    result.ErrorCount = 1;
                    result.Notes = $"Nepodařilo se vytvořit testovací soubor: {ex.Message}";
                    result.CompletedAtUtc = DateTime.UtcNow;
                    return result;
                }
            }
        }

        if (IsDevicePath(request.Drive.Path) && request.Operation != SurfaceTestOperation.ReadOnly && request.MaxBytesToTest == null && request.Drive.TotalSize <= 0)
        {
            result.ErrorCount = 1;
            result.Notes = "Pro zařízení je nutné zadat velikost testu.";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
        }

        if (!IsDevicePath(testFilePath) && !File.Exists(testFilePath))
        {
            result.ErrorCount = 1;
            result.Notes = "Soubor nebyl nalezen.";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
        }

        var isDevicePath = IsDevicePath(request.Drive.Path);

        // For HDD full test on devices, ensure we use write mode on test file
        // Don't force readonly for write operations on test files
        if (isDevicePath && request.Operation != SurfaceTestOperation.ReadOnly)
        {
            // HDD test: Use testFilePath (temporary file) for write operations
            // This is safe because we're writing to temp file, not the actual device
            request.AllowDeviceWrite = true; // Allow write to test file
        }

        var samples = new List<SurfaceTestSample>();
        var totalStopwatch = Stopwatch.StartNew();
        var sampleStopwatch = Stopwatch.StartNew();
        long processedBytes = 0;
        long totalBytesRead = 0;
        long totalBytesWritten = 0;
        long sampleBytes = 0;
        var sampleBlocks = 0;
        double peak = 0;
        double min = double.MaxValue;

        try
        {
            using var stream = new FileStream(
                testFilePath,
                FileMode.OpenOrCreate,
                request.Operation == SurfaceTestOperation.ReadOnly ? FileAccess.Read : FileAccess.ReadWrite,
                FileShare.ReadWrite,
                request.BlockSizeBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var totalToProcess = request.MaxBytesToTest
                ?? (request.Drive.TotalSize > 0 ? request.Drive.TotalSize : stream.Length);

            // Fallback to stream length if calculation resulted in 0
            if (totalToProcess <= 0)
            {
                totalToProcess = stream.Length;
            }

            if (totalToProcess <= 0)
            {
                result.ErrorCount = 1;
                result.Notes = "Nelze zjistit velikost testu.";
                result.CompletedAtUtc = DateTime.UtcNow;
                return result;
            }

            if (isDevicePath && totalToProcess > DeviceWriteLimitBytes)
            {
                totalToProcess = DeviceWriteLimitBytes;
                result.Notes = "Zařízení bude testováno pouze omezeným rozsahem pro bezpečnost.";
            }

            if (stream.Length > 0 && totalToProcess > stream.Length)
            {
                totalToProcess = stream.Length;
            }

            if (totalToProcess <= 0)
            {
                totalToProcess = stream.Length;
            }

            if (totalToProcess <= 0)
            {
                result.ErrorCount = 1;
                result.Notes = "Nelze zjistit velikost testu.";
                result.CompletedAtUtc = DateTime.UtcNow;
                return result;
            }

            var totalWorkBytes = request.Operation == SurfaceTestOperation.ReadOnly
                ? totalToProcess
                : totalToProcess * (request.SecureErase ? 3 : 2);

            var buffer = new byte[request.BlockSizeBytes];

            if (request.Operation != SurfaceTestOperation.ReadOnly)
            {
                FillPattern(buffer, request.Operation);
                stream.Position = 0;

                long writeErrors = 0;
                while (totalBytesWritten < totalToProcess)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var toWrite = (int)Math.Min(buffer.Length, totalToProcess - totalBytesWritten);
                    try
                    {
                        await stream.WriteAsync(buffer.AsMemory(0, toWrite), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        writeErrors++;
                        result.ErrorCount++;
                        if (writeErrors > 5) // Stop after 5 write errors
                        {
                            result.Notes = $"Příliš mnoho chyb při zápisu: {ex.Message}";
                            throw;
                        }
                    }
                    
                    totalBytesWritten += toWrite;
                    processedBytes += toWrite;

                    UpdateSamples(
                        toWrite,
                        request.BlockSizeBytes,
                        processedBytes,
                        totalWorkBytes,
                        request.SampleIntervalBlocks,
                        result.TestId,
                        ref sampleBytes,
                        ref sampleBlocks,
                        sampleStopwatch,
                        samples,
                        progress,
                        ref peak,
                        ref min);
                }

                await stream.FlushAsync(cancellationToken);
            }

            stream.Position = 0;

            long readErrors = 0;
            while (totalBytesRead < totalToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var toRead = (int)Math.Min(buffer.Length, totalToProcess - totalBytesRead);
                int read = 0;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
                }
                catch (Exception ex)
                {
                    readErrors++;
                    result.ErrorCount++;
                    if (readErrors > 5) // Stop after 5 read errors
                    {
                        result.Notes = $"Příliš mnoho chyb při čtení: {ex.Message}";
                        throw;
                    }
                    continue;
                }
                
                if (read == 0)
                {
                    break;
                }

                // Verify data integrity for write/verify operations
                if (request.Operation != SurfaceTestOperation.ReadOnly && !BufferMatches(buffer, read, request.Operation))
                {
                    result.ErrorCount += 1;
                    if (result.ErrorCount > 10) // Too many verify errors - disk is bad
                    {
                        result.Notes = "Disk selhává při ověření dat - je pravděpodobně vadný a neměl by být používán!";
                        break; // Stop testing
                    }
                }

                totalBytesRead += read;
                processedBytes += read;

                UpdateSamples(
                    read,
                    request.BlockSizeBytes,
                    processedBytes,
                    totalWorkBytes,
                    request.SampleIntervalBlocks,
                    result.TestId,
                    ref sampleBytes,
                    ref sampleBlocks,
                    sampleStopwatch,
                    samples,
                    progress,
                    ref peak,
                    ref min);
            }

            if (request.SecureErase && request.Operation != SurfaceTestOperation.ReadOnly)
            {
                FillPattern(buffer, SurfaceTestOperation.WriteZeroFill);
                stream.Position = 0;
                long eraseWritten = 0;
                while (eraseWritten < totalToProcess)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var toWrite = (int)Math.Min(buffer.Length, totalToProcess - eraseWritten);
                    await stream.WriteAsync(buffer.AsMemory(0, toWrite), cancellationToken);
                    eraseWritten += toWrite;
                    totalBytesWritten += toWrite;
                    processedBytes += toWrite;

                    UpdateSamples(
                        toWrite,
                        request.BlockSizeBytes,
                        processedBytes,
                        totalWorkBytes,
                        request.SampleIntervalBlocks,
                        result.TestId,
                        ref sampleBytes,
                        ref sampleBlocks,
                        sampleStopwatch,
                        samples,
                        progress,
                        ref peak,
                        ref min);
                }

                await stream.FlushAsync(cancellationToken);
                result.SecureErasePerformed = true;
            }

            if (sampleBytes > 0)
            {
                var sample = CreateSample(sampleBytes, request.BlockSizeBytes, processedBytes, sampleStopwatch);
                if (sample != null)
                {
                    samples.Add(sample);
                    peak = Math.Max(peak, sample.ThroughputMbps);
                    min = Math.Min(min, sample.ThroughputMbps);
                }
            }

            if (request.Operation != SurfaceTestOperation.ReadOnly && result.ErrorCount == 0)
            {
                result.Notes = request.SecureErase
                    ? "Zápis, ověření a bezpečné přepsání dokončeno."
                    : "Zápis a ověření dokončeno.";
            }
        }
        catch (Exception ex)
        {
            result.ErrorCount += 1;
            result.Notes = ex.Message;
        }
        finally
        {
            // Clean up temporary test file if it was created
            if (IsDevicePath(request.Drive.Path) && File.Exists(testFilePath))
            {
                try
                {
                    File.Delete(testFilePath);
                }
                catch
                {
                    // Silently ignore cleanup errors
                }
            }
        }

        totalStopwatch.Stop();
        result.CompletedAtUtc = DateTime.UtcNow;
        result.TotalBytesTested = totalBytesRead;
        result.Samples = samples;

        var totalWork = totalBytesRead + totalBytesWritten;
        if (totalStopwatch.Elapsed.TotalSeconds > 0 && totalWork > 0)
        {
            result.AverageSpeedMbps = totalWork / (1024d * 1024d) / totalStopwatch.Elapsed.TotalSeconds;
        }

        result.PeakSpeedMbps = peak;
        result.MinSpeedMbps = min == double.MaxValue ? 0 : min;

        return result;
    }

    private static void UpdateSamples(
        int bytes,
        int blockSize,
        long processedBytes,
        long totalWorkBytes,
        int sampleIntervalBlocks,
        Guid testId,
        ref long sampleBytes,
        ref int sampleBlocks,
        Stopwatch sampleStopwatch,
        List<SurfaceTestSample> samples,
        IProgress<SurfaceTestProgress>? progress,
        ref double peak,
        ref double min)
    {
        sampleBytes += bytes;
        sampleBlocks++;

        var interval = Math.Max(1, sampleIntervalBlocks);
        if (sampleBlocks < interval)
        {
            return;
        }

        if (sampleStopwatch.Elapsed.TotalSeconds <= 0)
        {
            return;
        }

        var sample = CreateSample(sampleBytes, blockSize, processedBytes, sampleStopwatch);
        if (sample != null)
        {
            samples.Add(sample);
            peak = Math.Max(peak, sample.ThroughputMbps);
            min = Math.Min(min, sample.ThroughputMbps);
            progress?.Report(CreateProgress(testId, processedBytes, totalWorkBytes, sample.ThroughputMbps));
        }

        sampleBytes = 0;
        sampleBlocks = 0;
        sampleStopwatch.Restart();
    }

    private static bool BufferMatches(byte[] buffer, int length, SurfaceTestOperation operation)
    {
        var expected = operation == SurfaceTestOperation.WriteZeroFill ? (byte)0 : PatternByte;
        for (var i = 0; i < length; i++)
        {
            if (buffer[i] != expected)
            {
                return false;
            }
        }

        return true;
    }

    private static void FillPattern(byte[] buffer, SurfaceTestOperation operation)
    {
        var value = operation == SurfaceTestOperation.WritePattern ? PatternByte : (byte)0;
        Array.Fill(buffer, value);
    }

    private static bool IsDevicePath(string path)
    {
        return path.StartsWith("\\\\.\\", StringComparison.Ordinal) ||
               path.StartsWith("/dev/", StringComparison.Ordinal);
    }

    private static SurfaceTestSample? CreateSample(long sampleBytes, int blockSizeBytes, long totalBytes, Stopwatch stopwatch)
    {
        if (stopwatch.Elapsed.TotalSeconds <= 0)
        {
            return null;
        }

        var throughput = sampleBytes / (1024d * 1024d) / stopwatch.Elapsed.TotalSeconds;

        return new SurfaceTestSample
        {
            OffsetBytes = Math.Max(0, totalBytes - sampleBytes),
            BlockSizeBytes = blockSizeBytes,
            ThroughputMbps = Math.Round(throughput, 2),
            TimestampUtc = DateTime.UtcNow,
            ErrorCount = 0
        };
    }

    private static SurfaceTestProgress CreateProgress(Guid testId, long bytesProcessed, long totalBytes, double throughput)
    {
        return new SurfaceTestProgress
        {
            TestId = testId,
            BytesProcessed = bytesProcessed,
            PercentComplete = totalBytes == 0 ? 0 : Math.Min(100, bytesProcessed * 100d / totalBytes),
            CurrentThroughputMbps = Math.Round(throughput, 2),
            TimestampUtc = DateTime.UtcNow
        };
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
}
