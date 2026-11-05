using System.Diagnostics;
using System.Globalization;
using DiskChecker.Core.Models;

namespace DiskChecker.TUI.Services;

/// <summary>Surface test work performed by the terminal front end.</summary>
public enum TuiSurfaceOperation
{
    /// <summary>Sequential read of the whole device (non destructive).</summary>
    ReadScan,

    /// <summary>Write a pattern and verify it by reading back (destructive).</summary>
    WriteVerify,
}

/// <summary>Options of a terminal surface test run.</summary>
public sealed record TuiSurfaceTestOptions(
    TuiSurfaceOperation Operation,
    long BlockSizeBytes = 1024 * 1024,
    long? MaxBytes = null,
    int SampleIntervalBlocks = 64);

/// <summary>Outcome of a terminal surface test run.</summary>
public sealed class TuiSurfaceTestReport
{
    public long TotalBytes { get; set; }

    public TuiSurfaceOperation Operation { get; set; }

    public long ProcessedBytes { get; set; }

    public int ErrorCount { get; set; }

    public double AverageSpeedMbps { get; set; }

    public double MinSpeedMbps { get; set; }

    public double MaxSpeedMbps { get; set; }

    public TimeSpan Duration { get; set; }

    public bool Completed { get; set; }

    public bool Cancelled { get; set; }

    public string? FailureMessage { get; set; }

    public List<SurfaceTestSample> Samples { get; } = new();
}

/// <summary>
/// Real sequential surface test with unbuffered friendly options.
/// The runner works on any path (block device or regular file) which keeps the
/// dangerous I/O testable against a temporary file in unit tests.
/// </summary>
public static class TuiSurfaceTestRunner
{
    public static async Task<TuiSurfaceTestReport> RunAsync(
        string path,
        long sizeBytes,
        TuiSurfaceTestOptions options,
        IProgress<SurfaceTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        var report = new TuiSurfaceTestReport { Operation = options.Operation };
        var started = DateTime.UtcNow;
        long limit = sizeBytes;
        if (options.MaxBytes is > 0 && options.MaxBytes < limit)
        {
            limit = options.MaxBytes.Value;
        }

        limit = Math.Max(0, limit);
        report.TotalBytes = limit;

        int blockSize = (int)Math.Clamp(options.BlockSizeBytes, 4096, 8 * 1024 * 1024);
        var buffer = new byte[blockSize];
        var verifyBuffer = new byte[blockSize];
        long processed = 0;
        var speeds = new List<double>();
        var window = Stopwatch.StartNew();
        long windowBytes = 0;
        long blocks = 0;
        int sampleInterval = Math.Max(1, options.SampleIntervalBlocks);

        try
        {
            if (options.Operation == TuiSurfaceOperation.ReadScan)
            {
                using var stream = OpenRead(path, blockSize);
                while (processed < limit)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int toRead = (int)Math.Min(blockSize, limit - processed);
                    int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    processed += read;
                    windowBytes += read;
                    blocks++;
                    Sample();
                }
            }
            else
            {
                using (var stream = OpenWrite(path, blockSize))
                {
                    while (processed < limit)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int toWrite = (int)Math.Min(blockSize, limit - processed);
                        FillPattern(buffer, toWrite, processed);
                        await stream.WriteAsync(buffer.AsMemory(0, toWrite), cancellationToken).ConfigureAwait(false);
                        processed += toWrite;
                        windowBytes += toWrite;
                        blocks++;
                        Sample();
                    }

                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                // Verify pass: read the same region back and compare.
                long verified = 0;
                using (var stream = OpenRead(path, blockSize))
                {
                    while (verified < processed)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int toRead = (int)Math.Min(blockSize, processed - verified);
                        int read = await stream.ReadAsync(verifyBuffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                        if (read <= 0)
                        {
                            report.ErrorCount++;
                            break;
                        }

                        if (!MatchesPattern(verifyBuffer, read, verified))
                        {
                            report.ErrorCount++;
                        }

                        verified += read;
                        windowBytes += read;
                        blocks++;
                        Sample();
                    }
                }
            }

            report.Completed = processed == limit && report.ErrorCount == 0;
            if (!report.Completed && report.FailureMessage is null)
                report.FailureMessage = "Incomplete disk scan or verification.";
        }
        catch (OperationCanceledException)
        {
            report.Cancelled = true;
        }
        catch (IOException ex)
        {
            report.FailureMessage = ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            report.FailureMessage = ex.Message;
        }
        finally
        {
            report.ProcessedBytes = processed;
            report.Duration = DateTime.UtcNow - started;
            if (speeds.Count > 0)
            {
                report.AverageSpeedMbps = speeds.Average();
                report.MinSpeedMbps = speeds.Min();
                report.MaxSpeedMbps = speeds.Max();
            }
        }

        return report;

        void Sample()
        {
            if (blocks % sampleInterval != 0 || window.ElapsedMilliseconds <= 0)
            {
                return;
            }

            double speed = windowBytes / (1024d * 1024d) / window.Elapsed.TotalSeconds;
            speeds.Add(speed);
            double percent = limit <= 0 ? 100 : Math.Clamp(processed * 100d / limit, 0, 100);
            report.Samples.Add(new SurfaceTestSample
            {
                OffsetBytes = processed,
                BlockSizeBytes = blockSize,
                ThroughputMbps = speed,
                ProgressPercent = percent,
                TimestampUtc = DateTime.UtcNow,
                Elapsed = DateTime.UtcNow - started,
                Phase = options.Operation == TuiSurfaceOperation.ReadScan ? "Read" : "Write",
                ErrorCount = report.ErrorCount,
            });

            progress?.Report(new SurfaceTestProgress
            {
                PercentComplete = percent,
                BytesProcessed = processed,
                CurrentThroughputMbps = speed,
                TotalElapsed = DateTime.UtcNow - started,
                Phase = options.Operation == TuiSurfaceOperation.ReadScan ? "Read" : "Write",
                TimestampUtc = DateTime.UtcNow,
            });

            windowBytes = 0;
            window.Restart();
        }
    }

    private static FileStream OpenRead(string path, int blockSize) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, blockSize, FileOptions.SequentialScan);

    private static FileStream OpenWrite(string path, int blockSize) =>
        new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, blockSize,
            FileOptions.SequentialScan | FileOptions.WriteThrough);

    private const ulong PatternSeed = 0x9E3779B97F4A7C15UL;

    private static void FillPattern(byte[] buffer, int length, long offset)
    {
        for (int index = 0; index < length; index++)
        {
            ulong value = unchecked(PatternSeed * (ulong)(offset + index) + PatternSeed);
            buffer[index] = (byte)(value >> 32);
        }
    }

    private static bool MatchesPattern(byte[] buffer, int length, long offset)
    {
        for (int index = 0; index < length; index++)
        {
            ulong value = unchecked(PatternSeed * (ulong)(offset + index) + PatternSeed);
            if (buffer[index] != (byte)(value >> 32))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Formats a throughput value for logs.</summary>
    public static string FormatSpeed(double megabytesPerSecond) =>
        megabytesPerSecond.ToString("F1", CultureInfo.InvariantCulture) + " MB/s";
}
