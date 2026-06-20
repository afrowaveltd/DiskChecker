using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Hardware;

/// <summary>
/// Cross-platform seek test executor using direct (unbuffered) device I/O.
/// Measures mechanical seek latency for HDDs via raw device access.
/// </summary>
public class SeekTestExecutor : ISeekTestExecutor
{
    private readonly ILogger<SeekTestExecutor>? _logger;
    private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// One unreported warm-up seek is executed before every seek test.
    /// On Windows the first direct device read can include handle/device cache spin-up and
    /// storage-stack initialization latency, which can dwarf real seek latency and distort charts.
    /// </summary>
    private const int WarmupSeekCount = 1;

    // ──────────────────────────────────────────────
    //  SMART-informed recommendation constants
    // ──────────────────────────────────────────────

    /// <summary>Default full-stroke seek count for a healthy young HDD.</summary>
    private const int DefaultFullStrokeSeeks = 2000;

    /// <summary>Default random seek count for a healthy young HDD.</summary>
    private const int DefaultRandomSeeks = 3000;

    /// <summary>Default skip seek count for a healthy young HDD.</summary>
    private const int DefaultSkipSeeks = 2000;

    /// <summary>Default skip segments (LBA chunks to jump).</summary>
    private const int DefaultSkipSegments = 1000;

    /// <summary>Maximum safe seek count for any disk (hard ceiling).</summary>
    private const int AbsoluteMaxSeeks = 5000;

    /// <summary>Power-on hours threshold for "aged" disk (3 years).</summary>
    private const int AgedHoursThreshold = 26280; // 3 * 365.25 * 24

    /// <summary>Power-on hours threshold for "old" disk (5 years).</summary>
    private const int OldHoursThreshold = 43800; // 5 * 365.25 * 24

    /// <summary>Power-on hours threshold for "veteran" disk (7+ years).</summary>
    private const int VeteranHoursThreshold = 61320; // 7 * 365.25 * 24

    /// <summary>Reallocated sector count that triggers conservative mode.</summary>
    private const int ReallocatedWarningThreshold = 5;

    /// <summary>Reallocated sector count that triggers fragile mode.</summary>
    private const int ReallocatedFragileThreshold = 50;

    /// <summary>Pending sector count that triggers fragile mode.</summary>
    private const int PendingFragileThreshold = 10;

    public SeekTestExecutor(ILogger<SeekTestExecutor>? logger = null)
    {
        _logger = logger;
    }

    // ──────────────────────────────────────────────
    //  Recommendation engine (SMART-informed)
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    public SeekTestRecommendation GetRecommendation(
        SmartaData? smartaData,
        long driveTotalBytes,
        bool isSolidState)
    {
        var rec = new SeekTestRecommendation
        {
            PowerOnHours = smartaData?.PowerOnHours,
            ReallocatedSectors = smartaData?.ReallocatedSectorCount,
            IsSolidState = isSolidState
        };

        // ── SSDs: seek tests are meaningless ──
        if (isSolidState)
        {
            rec.RecommendedType = SeekTestType.Random;
            rec.RecommendedSeekCount = 500;
            rec.RecommendedSkipSegments = 100;
            rec.MaxSafeSeekCount = 1000;
            rec.IsConservative = true;
            rec.Rationale = "SSD detekován. Seek testy měří mechanickou latenci, která u SSD nemá význam. " +
                            "Doporučen redukovaný random seek test (500 seeků) pro základní ověření I/O latence.";
            return rec;
        }

        var powerOnHours = smartaData?.PowerOnHours ?? 0;
        var reallocated = smartaData?.ReallocatedSectorCount ?? 0;
        var pending = smartaData?.PendingSectorCount ?? 0;

        // ── Fragile disk check ──
        if (reallocated >= ReallocatedFragileThreshold || pending >= PendingFragileThreshold)
        {
            rec.IsTooFragile = true;
            rec.RecommendedSeekCount = 0;
            rec.MaxSafeSeekCount = 0;
            rec.Rationale = $"Disk vykazuje kritické opotřebení: {reallocated} realokovaných sektorů, " +
                            $"{pending} pending sektorů. Seek test NENÍ doporučen – hrozí další poškození.";
            return rec;
        }

        // ── Veteran disk (7+ years / 80,000+ hours) ──
        if (powerOnHours >= VeteranHoursThreshold)
        {
            rec.RecommendedType = SeekTestType.FullStroke;
            rec.RecommendedSeekCount = 300;
            rec.RecommendedSkipSegments = 500;
            rec.MaxSafeSeekCount = 500;
            rec.IsConservative = true;
            rec.Rationale = $"Disk má {powerOnHours} provozních hodin ({(powerOnHours / (365.25 * 24)):F1} let). " +
                            "Veteránský disk – doporučen velmi šetrný full-stroke test (300 seeků). " +
                            "Intenzivní seekování by mohlo urychlit mechanické opotřebení.";
            return rec;
        }

        // ── Old disk (5-7 years) ──
        if (powerOnHours >= OldHoursThreshold)
        {
            rec.RecommendedType = SeekTestType.FullStroke;
            rec.RecommendedSeekCount = 600;
            rec.RecommendedSkipSegments = 800;
            rec.MaxSafeSeekCount = 1000;
            rec.IsConservative = true;
            rec.Rationale = $"Disk má {powerOnHours} provozních hodin ({(powerOnHours / (365.25 * 24)):F1} let). " +
                            "Starší disk – doporučen konzervativní full-stroke test (600 seeků).";
            return rec;
        }

        // ── Aged disk (3-5 years) with some reallocations ──
        if (powerOnHours >= AgedHoursThreshold && reallocated >= ReallocatedWarningThreshold)
        {
            rec.RecommendedType = SeekTestType.FullStroke;
            rec.RecommendedSeekCount = 800;
            rec.RecommendedSkipSegments = 1000;
            rec.MaxSafeSeekCount = 1500;
            rec.IsConservative = true;
            rec.Rationale = $"Disk má {powerOnHours} provozních hodin a {reallocated} realokovaných sektorů. " +
                            "Doporučen konzervativní full-stroke test (800 seeků).";
            return rec;
        }

        // ── Aged disk (3-5 years) clean ──
        if (powerOnHours >= AgedHoursThreshold)
        {
            rec.RecommendedType = SeekTestType.Random;
            rec.RecommendedSeekCount = 1500;
            rec.RecommendedSkipSegments = 1000;
            rec.MaxSafeSeekCount = 2500;
            rec.IsConservative = false;
            rec.Rationale = $"Disk má {powerOnHours} provozních hodin ({(powerOnHours / (365.25 * 24)):F1} let), " +
                            "ale SMART je čistý. Doporučen random seek test (1500 seeků) pro referenční měření.";
            return rec;
        }

        // ── Young/healthy disk ──
        rec.RecommendedType = SeekTestType.Random;
        rec.RecommendedSeekCount = DefaultRandomSeeks;
        rec.RecommendedSkipSegments = DefaultSkipSegments;
        rec.MaxSafeSeekCount = AbsoluteMaxSeeks;
        rec.IsConservative = false;
        rec.Rationale = powerOnHours > 0
            ? $"Disk má {powerOnHours} provozních hodin ({(powerOnHours / (365.25 * 24)):F1} let), SMART čistý. " +
              "Doporučen plný random seek test (3000 seeků) pro referenční měření."
            : "SMART data nedostupná. Doporučen výchozí random seek test (3000 seeků). " +
              "Pro přesnější doporučení spusťte nejprve SMART kontrolu.";
        return rec;
    }

    // ──────────────────────────────────────────────
    //  Platform support check
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    public Task<bool> IsPlatformSupportedAsync(CancellationToken cancellationToken = default)
    {
        // Both Linux and Windows are supported via direct device I/O
        var supported = IsLinux || IsWindows;
        if (!supported && _logger != null)
        {
            _logger.LogWarning("Seek test executor: unsupported platform (only Linux and Windows are supported)");
        }
        return Task.FromResult(supported);
    }

    // ──────────────────────────────────────────────
    //  Main execution entry point
    // ──────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<SeekTestResult> ExecuteAsync(
        SeekTestRequest request,
        Action<SeekTestProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new SeekTestResult
        {
            TestType = request.TestType,
            SeekCount = request.SeekCount,
            SkipSegments = request.SkipSegments,
            StartedAtUtc = DateTime.UtcNow
        };

        var totalWatch = Stopwatch.StartNew();

        try
        {
            // Generate one extra warm-up seek. The first measured device I/O can include OS/device
            // initialization overhead (especially on Windows) and is intentionally not reported.
            // The requested result count remains unchanged because the warm-up sample is discarded.
            var measuredSeekCount = Math.Max(1, request.SeekCount);
            var positions = GenerateSeekPositions(
                request.TestType,
                measuredSeekCount + WarmupSeekCount,
                request.Drive.TotalSize,
                request.SkipSegments,
                request.BlockSizeBytes);

            // Execute seeks with latency measurement on a background thread
            // (the loop is CPU-bound / synchronous I/O — running it on the UI thread
            //  would freeze the UI, preventing Dispatcher.UIThread.Post callbacks
            //  from being processed and leaving the user with no progress updates).
            var samples = await Task.Run(
                () => ExecuteSeeksAsync(
                    request.Drive.Path,
                    positions,
                    request.BlockSizeBytes,
                    request.CollectLatencySamples,
                    request.TimeoutSeconds,
                    progressCallback,
                    result,
                    cancellationToken),
                cancellationToken);

            result.Samples = samples;
            result.ErrorCount = samples.Count(s => s.HasError);

            // Compute statistics from successful samples
            var successful = samples.Where(s => !s.HasError).ToList();
            if (successful.Count > 0)
            {
                var latencies = successful.Select(s => s.LatencyMs).ToList();
                result.AverageLatencyMs = latencies.Average();
                result.MinLatencyMs = latencies.Min();
                result.MaxLatencyMs = latencies.Max();

                if (latencies.Count > 1)
                {
                    var avg = result.AverageLatencyMs;
                    var sumSq = latencies.Sum(l => (l - avg) * (l - avg));
                    result.LatencyStdDevMs = Math.Sqrt(sumSq / latencies.Count);
                }

                // Percentiles: sort and pick
                var sorted = latencies.OrderBy(l => l).ToList();
                result.MedianLatencyMs = Percentile(sorted, 0.50);
                result.P95LatencyMs = Percentile(sorted, 0.95);
                result.P99LatencyMs = Percentile(sorted, 0.99);
            }

            result.IsCompleted = !cancellationToken.IsCancellationRequested;
            result.WasAborted = cancellationToken.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            result.WasAborted = true;
            result.IsCompleted = false;
            result.Notes = "Test přerušen uživatelem.";
        }
        catch (Exception ex)
        {
            result.WasAborted = true;
            result.IsCompleted = false;
            result.Notes = $"Chyba během seek testu: {ex.Message}";
            _logger?.LogError(ex, "Seek test failed for {DrivePath}", request.Drive.Path);
        }
        finally
        {
            totalWatch.Stop();
            result.Duration = totalWatch.Elapsed;
            result.CompletedAtUtc = DateTime.UtcNow;
        }

        return result;
    }

    // ──────────────────────────────────────────────
    //  Statistics helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Computes a percentile from a sorted list using linear interpolation.
    /// </summary>
    private static double Percentile(List<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];

        double index = percentile * (sorted.Count - 1);
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);

        if (lower == upper) return sorted[lower];

        double fraction = index - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }

    // ──────────────────────────────────────────────
    //  Position generation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates seek position pairs based on test type.
    /// </summary>
    internal static List<(long SourceLba, long DestLba)> GenerateSeekPositions(
        SeekTestType testType,
        int seekCount,
        long totalBytes,
        int skipSegments,
        int blockSizeBytes)
    {
        if (totalBytes <= 0) totalBytes = 1_000_000_000_000; // 1 TB fallback
        if (seekCount <= 0) seekCount = 100;

        var maxLba = totalBytes / 512; // Convert bytes to LBA sectors
        if (maxLba < 1000) maxLba = 1000;

        // Ensure we don't read past the end
        var safeMaxLba = maxLba - (blockSizeBytes / 512) - 1;
        if (safeMaxLba < 100) safeMaxLba = 100;

        var positions = new List<(long SourceLba, long DestLba)>(seekCount);
        var rng = new Random(42); // Fixed seed for reproducibility

        switch (testType)
        {
            case SeekTestType.FullStroke:
                GenerateFullStrokePositions(positions, seekCount, safeMaxLba);
                break;

            case SeekTestType.Random:
                GenerateRandomPositions(positions, seekCount, safeMaxLba, rng);
                break;

            case SeekTestType.Skip:
                GenerateSkipPositions(positions, seekCount, safeMaxLba, skipSegments);
                break;

            default:
                GenerateRandomPositions(positions, seekCount, safeMaxLba, rng);
                break;
        }

        return positions;
    }

    private static void GenerateFullStrokePositions(
        List<(long SourceLba, long DestLba)> positions,
        int seekCount,
        long maxLba)
    {
        // Sweep from min to max and back, creating full-stroke seeks
        var minLba = 64L; // Skip first 64 sectors (MBR/GPT area)
        var range = maxLba - minLba;

        for (int i = 0; i < seekCount; i++)
        {
            // Alternate between forward sweep and backward sweep
            if (i % 2 == 0)
            {
                // Forward: min → max
                var fraction = (i / 2) / (double)(seekCount / 2);
                var dest = minLba + (long)(range * fraction);
                positions.Add((minLba, Math.Min(dest, maxLba)));
            }
            else
            {
                // Backward: max → min
                var fraction = (i / 2) / (double)(seekCount / 2);
                var dest = maxLba - (long)(range * fraction);
                positions.Add((maxLba, Math.Max(dest, minLba)));
            }
        }
    }

    private static void GenerateRandomPositions(
        List<(long SourceLba, long DestLba)> positions,
        int seekCount,
        long maxLba,
        Random rng)
    {
        var minLba = 64L;

        for (int i = 0; i < seekCount; i++)
        {
            var src = minLba + (long)(rng.NextDouble() * (maxLba - minLba));
            var dst = minLba + (long)(rng.NextDouble() * (maxLba - minLba));
            positions.Add((src, dst));
        }
    }

    private static void GenerateSkipPositions(
        List<(long SourceLba, long DestLba)> positions,
        int seekCount,
        long maxLba,
        int skipSegments)
    {
        var minLba = 64L;
        var segmentSize = Math.Max(1, (maxLba - minLba) / Math.Max(1, skipSegments));

        long currentPos = minLba;

        for (int i = 0; i < seekCount; i++)
        {
            // Jump forward by segmentSize * some multiplier
            var jumpSegments = 1 + (i % 10); // Vary jump size: 1,2,3,...,10 segments
            var dest = currentPos + (segmentSize * jumpSegments);

            if (dest > maxLba)
            {
                // Wrap around to beginning
                dest = minLba + (dest - maxLba);
                if (dest > maxLba) dest = minLba;
            }

            positions.Add((currentPos, dest));
            currentPos = dest;
        }
    }

    // ──────────────────────────────────────────────
    //  Seek execution with latency measurement
    // ──────────────────────────────────────────────

    private List<SeekLatencySample> ExecuteSeeksAsync(
        string devicePath,
        List<(long SourceLba, long DestLba)> positions,
        int blockSizeBytes,
        bool collectSamples,
        int timeoutSeconds,
        Action<SeekTestProgress>? progressCallback,
        SeekTestResult result,
        CancellationToken cancellationToken)
    {
        var expectedSamples = Math.Max(0, positions.Count - WarmupSeekCount);
        var samples = new List<SeekLatencySample>(expectedSamples);
        var totalSeeks = positions.Count;

        using var timeoutCts = timeoutSeconds > 0
            ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
            : new CancellationTokenSource();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        // Open the device for direct I/O
        var deviceHandle = OpenDeviceDirect(devicePath);
        if (deviceHandle == null || deviceHandle.IsInvalid)
        {
            _logger?.LogError("Failed to open device {DevicePath} for direct I/O", devicePath);
            // Return error samples for all positions
            for (int i = 0; i < totalSeeks; i++)
            {
                samples.Add(new SeekLatencySample
                {
                    Index = i + 1,
                    HasError = true,
                    ErrorMessage = $"Cannot open device: {devicePath}",
                    TimestampUtc = DateTime.UtcNow
                });
            }
            return samples;
        }

        try
        {
            // Allocate aligned buffer for direct I/O
            // O_DIRECT requires memory aligned to the device's logical block size (typically 512 bytes)
            const int alignment = 4096; // Use page-aligned for maximum compatibility
            byte[]? windowsBuffer = null;

            unsafe
            {
                void* alignedBuffer = null;

                try
                {
                    if (IsLinux)
                    {
                        // Use NativeMemory for aligned allocation (available in .NET 6+)
                        alignedBuffer = System.Runtime.InteropServices.NativeMemory.AlignedAlloc(
                            (nuint)blockSizeBytes, (nuint)alignment);
                        // Zero the buffer to avoid reading stale data
                        System.Runtime.InteropServices.NativeMemory.Clear(alignedBuffer, (nuint)blockSizeBytes);
                    }
                    else
                    {
                        windowsBuffer = new byte[blockSizeBytes];
                    }

                    // ── Pre-position head to a known sector (LBA 0) for consistent starting point ──
                    try
                    {
                        SeekToLba(deviceHandle, 0);
                        if (IsLinux)
                            ReadLinux(deviceHandle, (byte*)alignedBuffer, blockSizeBytes);
                        else
                            ReadFromDeviceWindows(deviceHandle, windowsBuffer!, blockSizeBytes);
                    }
                    catch
                    {
                        // Pre-positioning is best-effort; ignore failures
                    }

                    var lastProgressTime = Stopwatch.StartNew();
                    var progressInterval = TimeSpan.FromMilliseconds(200);

                    // Watchdog: per-seek timeout and consecutive-error tracking
                    const int perSeekTimeoutMs = 30_000;     // 30 s per individual seek operation
                    const int maxConsecutiveErrors = 10;    // abort after 10 consecutive failures
                    const int diskCheckThreshold = 5;       // after 5 consecutive errors, check if disk is still present
                    var consecutiveErrors = 0;
                    var diskStillPresent = true;

                    for (int i = 0; i < totalSeeks; i++)
                    {
                        linkedCts.Token.ThrowIfCancellationRequested();

                        var (sourceLba, destLba) = positions[i];
                        var isWarmupSeek = i < WarmupSeekCount;
                        var sample = new SeekLatencySample
                        {
                            Index = isWarmupSeek ? 0 : i - WarmupSeekCount + 1,
                            SourceLba = sourceLba,
                            DestinationLba = destLba,
                            SeekDistance = Math.Abs(destLba - sourceLba),
                            TimestampUtc = DateTime.UtcNow
                        };

                        try
                        {
                            // ── Watchdog: run the seek + read on a background thread with a hard timeout ──
                            // If the disk stops responding, the P/Invoke call can block for a very long time.
                            // We run it on a thread-pool thread and impose a per-seek timeout.  If it exceeds
                            // the timeout, we mark the seek as failed and continue — the disk is likely in
                            // trouble but we still want to collect as many samples as possible.
                            bool readOk = false;
                            Exception? ioException = null;

                            var seekTask = Task.Run(() =>
                            {
                                try
                                {
                                    // Seek to source position (pre-positioning)
                                    SeekToLba(deviceHandle, sourceLba);

                                    // Measure: seek to destination + read
                                    SeekToLba(deviceHandle, destLba);

                                    if (IsLinux)
                                        return ReadLinux(deviceHandle, (byte*)alignedBuffer, blockSizeBytes);
                                    else
                                        return ReadFromDeviceWindows(deviceHandle, windowsBuffer!, blockSizeBytes);
                                }
                                catch (Exception ex)
                                {
                                    ioException = ex;
                                    return false;
                                }
                            }, linkedCts.Token);

                            // Wait with timeout — if the disk is stuck, we don't hang forever
                            var seekWatch = Stopwatch.StartNew();
                            var completed = seekTask.Wait(TimeSpan.FromMilliseconds(perSeekTimeoutMs), linkedCts.Token);
                            seekWatch.Stop();

                            if (!completed)
                            {
                                // The seek timed out — the disk is likely unresponsive
                                sample.HasError = true;
                                sample.LatencyMs = seekWatch.Elapsed.TotalMilliseconds;
                                sample.ErrorMessage = $"Seek timed out after {perSeekTimeoutMs / 1000}s — disk unresponsive";
                                consecutiveErrors++;

                                // After a few consecutive timeouts/errors, check if the disk is still present
                                if (consecutiveErrors >= diskCheckThreshold && diskStillPresent)
                                {
                                    diskStillPresent = IsDevicePresent(devicePath);
                                    if (!diskStillPresent)
                                    {
                                        _logger?.LogError("Disk {DevicePath} disappeared during seek test at sample {Index}", devicePath, i + 1);

                                        // Mark all remaining reported samples as errors. Warm-up seek is never exposed.
                                        for (int j = Math.Max(i + 1, WarmupSeekCount); j < totalSeeks; j++)
                                        {
                                            samples.Add(new SeekLatencySample
                                            {
                                                Index = j - WarmupSeekCount + 1,
                                                HasError = true,
                                                ErrorMessage = "Disk disappeared — device no longer present",
                                                TimestampUtc = DateTime.UtcNow
                                            });
                                        }

                                        // Report progress with the failure
                                        if (progressCallback != null)
                                        {
                                            progressCallback(new SeekTestProgress
                                            {
                                                TestId = Guid.Parse(result.TestId),
                                                PercentComplete = 100.0,
                                                SeeksCompleted = expectedSamples,
                                                TotalSeeks = expectedSamples,
                                                CurrentAverageLatencyMs = 0,
                                                LatestSample = sample,
                                                TimestampUtc = DateTime.UtcNow
                                            });
                                        }

                                        result.WasAborted = true;
                                        result.Notes = $"Disk disappeared at sample {i + 1} — device {devicePath} no longer present";
                                        break;
                                    }
                                }

                                // If we've had too many consecutive errors, abort the test
                                if (consecutiveErrors >= maxConsecutiveErrors)
                                {
                                    _logger?.LogError("Aborting seek test: {Count} consecutive errors on {DevicePath}", consecutiveErrors, devicePath);

                                    // Mark all remaining reported samples as errors. Warm-up seek is never exposed.
                                    for (int j = Math.Max(i + 1, WarmupSeekCount); j < totalSeeks; j++)
                                    {
                                        samples.Add(new SeekLatencySample
                                        {
                                            Index = j - WarmupSeekCount + 1,
                                            HasError = true,
                                            ErrorMessage = $"Skipped — {consecutiveErrors} consecutive errors",
                                            TimestampUtc = DateTime.UtcNow
                                        });
                                    }

                                    result.WasAborted = true;
                                    result.Notes = $"Aborted after {consecutiveErrors} consecutive seek errors — disk may be faulty";

                                    if (progressCallback != null)
                                    {
                                        progressCallback(new SeekTestProgress
                                        {
                                            TestId = Guid.Parse(result.TestId),
                                            PercentComplete = 100.0,
                                            SeeksCompleted = expectedSamples,
                                            TotalSeeks = expectedSamples,
                                            CurrentAverageLatencyMs = 0,
                                            LatestSample = sample,
                                            TimestampUtc = DateTime.UtcNow
                                        });
                                    }
                                    break;
                                }
                            }
                            else
                            {
                                // Seek completed within timeout
                                sample.LatencyMs = seekWatch.Elapsed.TotalMilliseconds;
                                readOk = seekTask.Result;

                                if (ioException != null)
                                {
                                    sample.HasError = true;
                                    sample.ErrorMessage = ioException.Message;
                                    consecutiveErrors++;
                                }
                                else
                                {
                                    sample.HasError = !readOk;
                                    if (!readOk)
                                    {
                                        sample.ErrorMessage = "Read failed at destination LBA";
                                        consecutiveErrors++;
                                    }
                                    else
                                    {
                                        consecutiveErrors = 0;  // Reset on success
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            sample.HasError = true;
                            sample.ErrorMessage = ex.Message;
                            consecutiveErrors++;
                        }

                        if (isWarmupSeek)
                        {
                            // The first direct I/O is only a warm-up. Do not add it to results,
                            // statistics, progress callbacks, or charts. Also reset transient error
                            // tracking so a one-off warm-up delay does not poison the real test.
                            consecutiveErrors = 0;
                            continue;
                        }

                        samples.Add(sample);

                        // Progress reporting (includes latest reported sample for real-time UI)
                        if (progressCallback != null && lastProgressTime.Elapsed >= progressInterval)
                        {
                            var successful = samples.Where(s => !s.HasError).ToList();
                            var avgLatency = successful.Count > 0
                                ? successful.Average(s => s.LatencyMs)
                                : 0.0;
                            var reportedCompleted = samples.Count;

                            progressCallback(new SeekTestProgress
                            {
                                TestId = Guid.Parse(result.TestId),
                                PercentComplete = expectedSamples > 0 ? (double)reportedCompleted / expectedSamples * 100.0 : 100.0,
                                SeeksCompleted = reportedCompleted,
                                TotalSeeks = expectedSamples,
                                CurrentAverageLatencyMs = avgLatency,
                                LatestSample = sample,
                                TimestampUtc = DateTime.UtcNow
                            });

                            lastProgressTime.Restart();
                        }
                    }
                }
                finally
                {
                    if (alignedBuffer != null)
                    {
                        System.Runtime.InteropServices.NativeMemory.AlignedFree(alignedBuffer);
                    }
                }
            }
        }
        finally
        {
            deviceHandle.Dispose();
        }

        return samples;
    }

    // ──────────────────────────────────────────────
    //  Disk presence check
    // ──────────────────────────────────────────────

    /// <summary>
    /// Checks whether the device is still present and accessible.
    /// Used when a disk becomes unresponsive during seek testing.
    /// </summary>
    private static bool IsDevicePresent(string devicePath)
    {
        try
        {
            if (IsLinux)
            {
                // On Linux, check if the device node exists
                return System.IO.File.Exists(devicePath);
            }
            else
            {
                // On Windows, try to open the device briefly
                var convertedPath = ConvertToWindowsDevicePath(devicePath);
                var handle = CreateFileW(
                    convertedPath,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    return false;
                CloseHandle(handle);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    // ──────────────────────────────────────────────
    //  Platform-specific direct device I/O
    // ──────────────────────────────────────────────

    /// <summary>
    /// Opens a disk device for direct (unbuffered) I/O.
    /// Returns a SafeHandle-based wrapper for the file descriptor/handle.
    /// </summary>
    private static DeviceFileHandle OpenDeviceDirect(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            return DeviceFileHandle.Invalid;

        if (IsLinux)
            return OpenDeviceLinux(devicePath);
        if (IsWindows)
            return OpenDeviceWindows(devicePath);

        return DeviceFileHandle.Invalid;
    }

    /// <summary>
    /// Seeks to a specific LBA position on the device.
    /// </summary>
    private static void SeekToLba(DeviceFileHandle handle, long lba)
    {
        var byteOffset = lba * 512;
        if (IsLinux)
            SeekLinux(handle, byteOffset);
        else if (IsWindows)
            SeekWindows(handle, byteOffset);
    }

    /// <summary>
    /// Reads from the device at the current position (Windows byte[] overload).
    /// </summary>
    private static bool ReadFromDeviceWindows(DeviceFileHandle handle, byte[] buffer, int count)
    {
        return ReadWindows(handle, buffer, count);
    }

    // ── Linux P/Invoke ──

    private const int O_RDONLY = 0;
    private const int O_DIRECT = 0x4000;   // 16384 – bypass page cache
    private const int O_SYNC = 0x1000;     // 4096 – synchronous I/O
    private const int SEEK_SET = 0;

    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int open(string pathname, int flags, int mode);

    [DllImport("libc", SetLastError = true)]
    private static extern long lseek(int fd, long offset, int whence);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe nint read(int fd, byte* buf, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    private static DeviceFileHandle OpenDeviceLinux(string devicePath)
    {
        var fd = open(devicePath, O_RDONLY | O_DIRECT | O_SYNC, 0);
        if (fd < 0)
        {
            // Fallback: try without O_DIRECT (some devices don't support it)
            fd = open(devicePath, O_RDONLY | O_SYNC, 0);
        }
        return new DeviceFileHandle(fd, isLinux: true);
    }

    private static void SeekLinux(DeviceFileHandle handle, long byteOffset)
    {
        var result = lseek(handle.FileDescriptor, byteOffset, SEEK_SET);
        if (result < 0)
        {
            var errno = Marshal.GetLastWin32Error();
            throw new IOException($"lseek failed: errno={errno}, offset={byteOffset}");
        }
    }

    private static unsafe bool ReadLinux(DeviceFileHandle handle, byte* buffer, int count)
    {
        var bytesRead = read(handle.FileDescriptor, buffer, (nint)count);
        if (bytesRead < 0)
        {
            var errno = Marshal.GetLastWin32Error();
            // EIO (5) = I/O error, EINVAL (22) = alignment issue – expected for some sectors
            return false;
        }
        return bytesRead > 0;
    }

    // ── Windows P/Invoke ──

    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
    private const uint FILE_BEGIN = 0;
    private const uint INVALID_SET_FILE_POINTER = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(
        IntPtr hFile,
        long liDistanceToMove,
        out long lpNewFilePointer,
        uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        IntPtr hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static DeviceFileHandle OpenDeviceWindows(string devicePath)
    {
        // Convert Linux-style path to Windows PhysicalDrive path if needed
        var winPath = ConvertToWindowsDevicePath(devicePath);

        var handle = CreateFileW(
            winPath,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH,
            IntPtr.Zero);

        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            // Fallback: try without NO_BUFFERING
            handle = CreateFileW(
                winPath,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_WRITE_THROUGH,
                IntPtr.Zero);
        }

        return new DeviceFileHandle(handle, isLinux: false);
    }

    private static void SeekWindows(DeviceFileHandle handle, long byteOffset)
    {
        if (!SetFilePointerEx(handle.WindowsHandle, byteOffset, out _, FILE_BEGIN))
        {
            var error = Marshal.GetLastWin32Error();
            throw new IOException($"SetFilePointerEx failed: error={error}, offset={byteOffset}");
        }
    }

    private static bool ReadWindows(DeviceFileHandle handle, byte[] buffer, int count)
    {
        if (!ReadFile(handle.WindowsHandle, buffer, (uint)count, out uint bytesRead, IntPtr.Zero))
        {
            return false;
        }
        return bytesRead > 0;
    }

    private static string ConvertToWindowsDevicePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return @"\\.\PhysicalDrive0";

        // Already a Windows PhysicalDrive path
        if (path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase))
            return path;

        // /dev/sda → \\.\PhysicalDrive0 mapping
        // /dev/pd0 → \\.\PhysicalDrive0
        if (path.StartsWith("/dev/pd", StringComparison.OrdinalIgnoreCase))
        {
            var numStr = path.Substring(7);
            if (int.TryParse(numStr, out var num))
                return $@"\\.\PhysicalDrive{num}";
        }

        if (path.StartsWith("/dev/sd", StringComparison.OrdinalIgnoreCase))
        {
            // /dev/sda = 0, /dev/sdb = 1, etc.
            if (path.Length >= 8)
            {
                var letter = path[7];
                if (letter >= 'a' && letter <= 'z')
                    return $@"\\.\PhysicalDrive{letter - 'a'}";
            }
        }

        if (path.StartsWith("/dev/nvme", StringComparison.OrdinalIgnoreCase))
        {
            // Extract NVMe number
            var digits = new string(path.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var nvmeNum))
                return $@"\\.\PhysicalDrive{nvmeNum}";
        }

        return path;
    }

    // ──────────────────────────────────────────────
    //  Device file handle wrapper
    // ──────────────────────────────────────────────

    private sealed class DeviceFileHandle : IDisposable
    {
        private readonly bool _isLinux;
        private int _fd;
        private IntPtr _handle;
        private bool _disposed;

        public int FileDescriptor => _fd;
        public IntPtr WindowsHandle => _handle;
        public bool IsInvalid => _isLinux ? _fd < 0 : (_handle == IntPtr.Zero || _handle == new IntPtr(-1));

        public static DeviceFileHandle Invalid => new(-1, isLinux: true);

        public DeviceFileHandle(int fd, bool isLinux)
        {
            _isLinux = isLinux;
            _fd = fd;
            _handle = IntPtr.Zero;
        }

        public DeviceFileHandle(IntPtr handle, bool isLinux)
        {
            _isLinux = isLinux;
            _fd = -1;
            _handle = handle;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_isLinux)
            {
                if (_fd >= 0)
                {
                    _ = close(_fd);
                    _fd = -1;
                }
            }
            else
            {
                if (_handle != IntPtr.Zero && _handle != new IntPtr(-1))
                {
                    CloseHandle(_handle);
                    _handle = IntPtr.Zero;
                }
            }
        }
    }
}
