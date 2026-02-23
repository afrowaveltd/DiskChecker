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

        if (IsDevicePath(request.Drive.Path) && request.Operation != SurfaceTestOperation.ReadOnly && request.MaxBytesToTest == null && request.Drive.TotalSize <= 0)
        {
            result.ErrorCount = 1;
            result.Notes = "Pro zařízení je nutné zadat velikost testu.";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
        }

        if (!IsDevicePath(request.Drive.Path) && !File.Exists(request.Drive.Path))
        {
            result.ErrorCount = 1;
            result.Notes = "Soubor nebyl nalezen.";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
        }

        var isDevicePath = IsDevicePath(request.Drive.Path);

        if (isDevicePath && request.Operation != SurfaceTestOperation.ReadOnly)
        {
            result.ErrorCount = 1;
            result.Notes = "Zápis na zařízení není zatím podporován. Použijte test nad souborem.";
            result.CompletedAtUtc = DateTime.UtcNow;
            return result;
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
                request.Drive.Path,
                FileMode.Open,
                request.Operation == SurfaceTestOperation.ReadOnly ? FileAccess.Read : FileAccess.ReadWrite,
                FileShare.ReadWrite,
                request.BlockSizeBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var totalToProcess = request.MaxBytesToTest
                ?? (request.Drive.TotalSize > 0 ? request.Drive.TotalSize : (isDevicePath ? 0 : stream.Length));

            if (totalToProcess <= 0 && isDevicePath)
            {
                result.ErrorCount = 1;
                result.Notes = "Nelze zjistit velikost zařízení pro test.";
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

                while (totalBytesWritten < totalToProcess)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var toWrite = (int)Math.Min(buffer.Length, totalToProcess - totalBytesWritten);
                    await stream.WriteAsync(buffer.AsMemory(0, toWrite), cancellationToken);
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

            while (totalBytesRead < totalToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var toRead = (int)Math.Min(buffer.Length, totalToProcess - totalBytesRead);
                var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (request.Operation != SurfaceTestOperation.ReadOnly && !BufferMatches(buffer, read, request.Operation))
                {
                    result.ErrorCount += 1;
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
}
