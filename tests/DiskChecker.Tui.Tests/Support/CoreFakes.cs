using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.TUI.Services;

namespace DiskChecker.Tui.Tests.Support;

/// <summary>Configurable SMART provider used by the terminal screen tests.</summary>
public sealed class FakeSmartaProvider : IAdvancedSmartaProvider
{
    public SmartaData? Data { get; set; }

    public SmartaSelfTestStatus SelfTestStatus { get; set; } = SmartaSelfTestStatus.CompletedWithoutError;

    public bool SelfTestStarted { get; private set; }

    public SmartaSelfTestType? LastStartedType { get; private set; }

    public bool LastOperationWasPermissionDenied => false;

    public Task<SmartaData?> GetSmartaDataAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Data);

    public Task<bool> IsDriveValidAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<List<string>> ListDrivesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<string>());

    public Task<string> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult("smartmontools");

    public Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<int?> GetTemperatureOnlyAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Data?.Temperature);

    public Task<bool> IsSmartSupportedAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Data is not null);

    public Task<SmartaData?> GetAdvancedSmartaDataAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Data);

    public Task<List<SmartaAttributeItem>> GetSmartAttributesAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Data?.Attributes ?? new List<SmartaAttributeItem>());

    public Task<SmartaSelfTestStatus> GetSelfTestStatusAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(SelfTestStatus);

    public Task<List<SmartaSelfTestEntry>> GetSelfTestLogAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Data?.SelfTests ?? new List<SmartaSelfTestEntry>());

    public Task<List<SmartaMaintenanceAction>> GetSupportedMaintenanceActionsAsync(string devicePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<SmartaMaintenanceAction>());

    public Task<bool> ExecuteMaintenanceActionAsync(string devicePath, SmartaMaintenanceAction action, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> StartSelfTestAsync(string devicePath, SmartaSelfTestType testType, CancellationToken cancellationToken = default)
    {
        SelfTestStarted = true;
        LastStartedType = testType;
        return Task.FromResult(true);
    }

    public Task SetCacheTtlMinutesAsync(int minutes, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ClearSmartCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveSmartCacheForDeviceAsync(string devicePath, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveSmartCacheForSerialAsync(string serialNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<(int Hits, int Misses, int Items)> GetSmartCacheStatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((0, 0, 0));

    /// <summary>Builds a healthy 500 GB HDD sample.</summary>
    public static SmartaData Sample(string model = "TestDisk 500GB", bool healthy = true, int temperature = 34) => new()
    {
        DeviceModel = model,
        SerialNumber = "SN-TEST-0001",
        FirmwareVersion = "1.0",
        DeviceType = "SATA/ATA",
        IsHealthy = healthy,
        SmartEnabled = true,
        Temperature = temperature,
        PowerOnHours = 1200,
        PowerCycleCount = 42,
        ReallocatedSectorCount = 0,
        PendingSectorCount = 0,
        UncorrectableErrorCount = 0,
        TotalSize = 500L * 1024 * 1024 * 1024,
        Attributes = new List<SmartaAttributeItem>
        {
            new() { Id = 5, Name = "Reallocated_Sector_Ct", Current = 100, Worst = 100, Threshold = 10, IsOk = true },
            new() { Id = 194, Name = "Temperature_Celsius", Current = 34, Worst = 40, Threshold = 0, IsOk = true },
        },
        SelfTests = new List<SmartaSelfTestEntry>
        {
            new() { Number = 1, Type = SmartaSelfTestType.ShortTest, Status = SmartaSelfTestStatus.CompletedWithoutError, LifeTimeHours = 1200 },
        },
    };
}

/// <summary>Fake sanitization service that reports progress and returns a result.</summary>
public sealed class FakeSanitizationService : IDiskSanitizationService
{
    public bool FailWrite { get; set; }

    public bool ThrowOnSanitize { get; set; }

    public int SanitizeCalls { get; private set; }

    public int? LastPacingSeconds { get; private set; }

    public int PartitionCalls { get; private set; }

    public Task<SanitizationResult> SanitizeDiskPacedAsync(
        string devicePath, long diskSize, bool createPartition, bool format,
        string volumeLabel, SanitizationWritePacing pacing,
        IProgress<SanitizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LastPacingSeconds = (int)pacing.PauseDuration.TotalSeconds;
        return SanitizeDiskAsync(devicePath, diskSize, createPartition, format,
            volumeLabel, progress, cancellationToken);
    }

    public Task<SanitizationResult> SanitizeDiskAsync(
        string devicePath,
        long diskSize,
        bool createPartition = true,
        bool format = true,
        string volumeLabel = "Sanitized",
        IProgress<SanitizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        SanitizeCalls++;
        if (ThrowOnSanitize)
        {
            throw new IOException("simulované selhání zařízení");
        }

        for (int step = 1; step <= 4; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new SanitizationProgress
            {
                PhaseKind = step <= 2 ? SanitizationProgressPhase.Write : SanitizationProgressPhase.ReadVerify,
                Phase = step <= 2 ? "Write zeros" : "Read and verify",
                ProgressPercent = step * 25,
                BytesProcessed = diskSize * step / 4,
                TotalBytes = diskSize,
                CurrentSpeedMBps = 180 + step,
                Errors = FailWrite ? 2 : 0,
                TotalElapsed = TimeSpan.FromSeconds(step),
            });
        }

        return Task.FromResult(new SanitizationResult
        {
            Success = !FailWrite,
            BytesWritten = diskSize,
            BytesRead = diskSize,
            WriteSpeedMBps = 182,
            ReadSpeedMBps = 179,
            ErrorsDetected = FailWrite ? 2 : 0,
            Duration = TimeSpan.FromSeconds(4),
            PartitionCreated = createPartition,
            FileSystem = format ? "ext4" : null,
        });
    }

    public Task<SanitizationResult> CreatePartitionAsync(
        string devicePath,
        string volumeLabel = "Tested",
        bool format = true,
        IProgress<SanitizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        PartitionCalls++;
        return Task.FromResult(new SanitizationResult
        {
            Success = true,
            PartitionCreated = true,
            Formatted = format,
            FileSystem = "ext4",
            VolumeLabel = volumeLabel,
        });
    }
}

/// <summary>Fake seek executor returning deterministic latency samples.</summary>
public sealed class FakeSeekExecutor : ISeekTestExecutor
{
    public bool TooFragile { get; set; }

    public int SeekCountOverride { get; set; }

    public Task<SeekTestResult> ExecuteAsync(
        SeekTestRequest request,
        Action<SeekTestProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        int count = SeekCountOverride > 0 ? Math.Min(SeekCountOverride, request.SeekCount) : Math.Min(20, request.SeekCount);
        var samples = new List<SeekLatencySample>(count);
        for (int index = 1; index <= count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double latency = 9 + (index % 5) * 0.4;
            samples.Add(new SeekLatencySample
            {
                Index = index,
                LatencyMs = latency,
                SourceLba = index * 512,
                DestinationLba = (index * 7) * 512,
                SeekDistance = 6 * 512,
                TimestampUtc = DateTime.UtcNow,
            });

            progressCallback?.Invoke(new SeekTestProgress
            {
                PercentComplete = index * 100d / count,
                SeeksCompleted = index,
                TotalSeeks = count,
                CurrentAverageLatencyMs = latency,
                LatestSample = samples[^1],
                Elapsed = TimeSpan.FromMilliseconds(index * 10),
            });
        }

        var latencies = samples.Select(sample => sample.LatencyMs).OrderBy(value => value).ToList();
        return Task.FromResult(new SeekTestResult
        {
            DriveModel = request.Drive.Name,
            DrivePath = request.Drive.Path,
            DriveTotalBytes = request.Drive.TotalSize,
            TestType = request.TestType,
            SeekCount = count,
            Samples = samples,
            IsCompleted = true,
            Duration = TimeSpan.FromSeconds(1),
            AverageLatencyMs = latencies.Average(),
            MinLatencyMs = latencies[0],
            MaxLatencyMs = latencies[^1],
            MedianLatencyMs = latencies[latencies.Count / 2],
            P95LatencyMs = latencies[Math.Max(0, (int)(latencies.Count * 0.95) - 1)],
            P99LatencyMs = latencies[Math.Max(0, (int)(latencies.Count * 0.99) - 1)],
            LatencyStdDevMs = 0.5,
        });
    }

    public SeekTestRecommendation GetRecommendation(SmartaData? smartaData, long driveTotalBytes, bool isSolidState) => new()
    {
        RecommendedType = SeekTestType.Random,
        RecommendedSeekCount = 1000,
        RecommendedSkipSegments = 1000,
        MaxSafeSeekCount = 5000,
        Rationale = "Testovací doporučení",
        IsTooFragile = TooFragile,
        IsConservative = false,
        IsSolidState = isSolidState,
    };

    public Task<bool> IsPlatformSupportedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}

/// <summary>Fake surface runner producing deterministic samples.</summary>
public sealed class FakeSurfaceRunner : ITuiSurfaceTestRunner
{
    public TuiSurfaceTestReport? Report { get; set; }

    public Task<TuiSurfaceTestReport> RunAsync(
        string path,
        long sizeBytes,
        TuiSurfaceTestOptions options,
        IProgress<SurfaceTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var report = Report ?? new TuiSurfaceTestReport
        {
            TotalBytes = sizeBytes,
            ProcessedBytes = sizeBytes,
            Completed = true,
            Duration = TimeSpan.FromSeconds(2),
            AverageSpeedMbps = 150,
            MinSpeedMbps = 120,
            MaxSpeedMbps = 180,
        };

        for (int step = 1; step <= 4; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new SurfaceTestProgress
            {
                PercentComplete = step * 25,
                BytesProcessed = sizeBytes * step / 4,
                CurrentThroughputMbps = 140 + step,
                TotalElapsed = TimeSpan.FromSeconds(step),
                Phase = "Read",
            });
        }

        return Task.FromResult(report);
    }
}

/// <summary>Fake disk detection service.</summary>
public sealed class FakeDiskDetectionService : IDiskDetectionService
{
    public List<CoreDriveInfo> Drives { get; } = new();

    public Task<IReadOnlyList<CoreDriveInfo>> GetDrivesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CoreDriveInfo>>(Drives);

    public Task<CoreDriveInfo?> GetDriveAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(Drives.FirstOrDefault(drive => drive.Path == path));

    public Task<(int? SpeedMbps, string? Description)> DetectConnectionSpeedAsync(
        string devicePath,
        CoreBusType busType,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<(int?, string?)>((6000, "SATA 6 Gb/s"));

    public static CoreDriveInfo Sample(string path = "/dev/sdz", string name = "TestDisk 500GB", long size = 500L * 1024 * 1024 * 1024) => new()
    {
        Path = path,
        Name = name,
        Model = name,
        SerialNumber = "SN-TEST-0001",
        FirmwareVersion = "1.0",
        TotalSize = size,
        BusType = CoreBusType.Sata,
        IsPhysical = true,
        IsReady = true,
        SupportsSmart = true,
        Interface = "SATA",
    };
}
