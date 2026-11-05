using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Sequential read-only scan of a disk or disk-image file.
/// Never opens a writable handle or compares the contents to a predefined pattern.
/// </summary>
public static class SurfaceReadScanner
{
    private const int BlockSize = 1024 * 1024;
    private const long MinimumSampleInterval = 16L * 1024 * 1024;
    private const long MaximumSamples = 4000;

    public static async Task<SurfaceTestResult> ScanAsync(
        string path,
        long deviceSizeBytes,
        long? maxBytes,
        IProgress<SurfaceTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviceSizeBytes);
        if (maxBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        long targetBytes = Math.Min(deviceSizeBytes, maxBytes ?? deviceSizeBytes);
        long sampleInterval = Math.Max(MinimumSampleInterval, targetBytes / MaximumSamples);
        var result = new SurfaceTestResult
        {
            StartedAtUtc = DateTime.UtcNow,
            DriveTotalBytes = deviceSizeBytes,
            Operation = SurfaceTestOperation.ReadOnly,
            FullDiskRead = targetBytes == deviceSizeBytes
        };
        long processed = 0;
        long sampledBytes = 0;
        var lastSampleTime = TimeSpan.Zero;
        double minSpeed = double.MaxValue;
        double maxSpeed = 0;
        var clock = Stopwatch.StartNew();
        var buffer = new byte[BlockSize];

        void RecordSample()
        {
            if (sampledBytes == 0)
                return;
            TimeSpan elapsed = clock.Elapsed;
            double seconds = Math.Max(0.000001, (elapsed - lastSampleTime).TotalSeconds);
            double speed = sampledBytes / (1024d * 1024d) / seconds;
            minSpeed = Math.Min(minSpeed, speed);
            maxSpeed = Math.Max(maxSpeed, speed);
            double percent = processed * 100d / targetBytes;
            result.Samples.Add(new SurfaceTestSample
            {
                OffsetBytes = processed,
                BlockSizeBytes = (int)Math.Min(sampledBytes, int.MaxValue),
                ThroughputMbps = speed,
                ProgressPercent = percent,
                TimestampUtc = DateTime.UtcNow,
                Elapsed = elapsed,
                Phase = "Read",
                ErrorCount = result.ErrorCount
            });
            progress?.Report(new SurfaceTestProgress
            {
                PercentComplete = percent,
                BytesProcessed = processed,
                CurrentThroughputMbps = speed,
                TotalElapsed = elapsed,
                Phase = "Read",
                TimestampUtc = DateTime.UtcNow
            });
            sampledBytes = 0;
            lastSampleTime = elapsed;
        }

        try
        {
            // The OS handle itself enforces no-write access.
            await using var stream = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite, BlockSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (processed < targetBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min(BlockSize, targetBytes - processed);
                int read = await stream.ReadAsync(buffer.AsMemory(0, count), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    result.ErrorCount++;
                    result.Notes = "Unexpected end of device at byte offset " + processed
                        + "; expected " + targetBytes + " bytes.";
                    break;
                }
                processed += read;
                sampledBytes += read;
                if (sampledBytes >= sampleInterval || processed == targetBytes)
                    RecordSample();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            result.ErrorCount++;
            result.Notes = "Read I/O failure at byte offset " + processed + ": " + ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            result.ErrorCount++;
            result.Notes = "Read access denied: " + ex.Message;
        }
        finally
        {
            RecordSample();
            clock.Stop();
            result.TotalBytesTested = processed;
            result.CompletedAtUtc = DateTime.UtcNow;
            result.MinSpeedMbps = minSpeed == double.MaxValue ? 0 : minSpeed;
            result.PeakSpeedMbps = maxSpeed;
            result.AverageSpeedMbps = clock.Elapsed.TotalSeconds > 0
                ? processed / (1024d * 1024d) / clock.Elapsed.TotalSeconds : 0;
        }

        if (result.ErrorCount == 0 && processed == targetBytes)
            result.Notes = "Read-only scan completed: " + processed + " / " + targetBytes + " bytes.";
        else if (result.ErrorCount == 0)
        {
            result.ErrorCount = 1;
            result.Notes = "Incomplete scan: " + processed + " / " + targetBytes + " bytes.";
        }
        return result;
    }
}
