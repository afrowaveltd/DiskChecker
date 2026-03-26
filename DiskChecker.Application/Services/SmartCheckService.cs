using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Application.Services;

/// <summary>
/// Executes SMART checks and persists the results.
/// </summary>
public class SmartCheckService
{
    private const string SmartCheckTestType = "SmartCheck";
    private static readonly Action<ILogger, string, Exception?> LogSmartDataUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(SmartCheckService)),
            "SMART data could not be retrieved for {DrivePath}.");

    private static readonly Action<ILogger, Exception?> LogDiskCardSaveFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(SmartCheckService)),
            "Failed to save SMART check to disk card");

    private readonly ISmartaProvider _smartaProvider;
    private readonly IQualityCalculator _qualityCalculator;
    private readonly DiskCheckerDbContext _dbContext;
    private readonly DiskCardTestService _cardTestService;
    private readonly ILogger<SmartCheckService> _logger;

    private IAdvancedSmartaProvider? AdvancedProvider => _smartaProvider as IAdvancedSmartaProvider;

    public SmartCheckService(
        ISmartaProvider smartaProvider,
        IQualityCalculator qualityCalculator,
        DiskCheckerDbContext dbContext,
        DiskCardTestService cardTestService,
        ILogger<SmartCheckService> logger)
    {
        _smartaProvider = smartaProvider;
        _qualityCalculator = qualityCalculator;
        _dbContext = dbContext;
        _cardTestService = cardTestService;
        _logger = logger;
    }

    public async Task<SmartCheckResult?> RunAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);

        var smartaData = await _smartaProvider.GetSmartaDataAsync(drive.Path, cancellationToken);
        if (smartaData == null)
        {
            LogSmartDataUnavailable(_logger, drive.Path, null);
            return null;
        }

        if (DriveIdentityResolver.IsReliableSerialNumber(smartaData.SerialNumber))
        {
            drive.SerialNumber = DriveIdentityResolver.NormalizeSerial(smartaData.SerialNumber);
        }

        var rating = _qualityCalculator.CalculateQuality(smartaData);
        IReadOnlyList<SmartaAttributeItem> attributes = Array.Empty<SmartaAttributeItem>();
        SmartaSelfTestStatus? selfTestStatus = null;
        IReadOnlyList<SmartaSelfTestEntry> selfTestLog = Array.Empty<SmartaSelfTestEntry>();

        if (AdvancedProvider != null)
        {
            attributes = await AdvancedProvider.GetSmartAttributesAsync(drive.Path, cancellationToken);
            selfTestStatus = await AdvancedProvider.GetSelfTestStatusAsync(drive.Path, cancellationToken);
            selfTestLog = await AdvancedProvider.GetSelfTestLogAsync(drive.Path, cancellationToken);
        }

        var testDate = DateTime.UtcNow;

        var serialKey = DriveIdentityResolver.BuildIdentityKey(
            drive.Path,
            smartaData.SerialNumber,
            smartaData.DeviceModel ?? drive.Name,
            smartaData.FirmwareVersion);

        var driveRecord = await _dbContext.Drives
            .SingleOrDefaultAsync(d => d.SerialNumber == serialKey, cancellationToken);

        if (driveRecord == null)
        {
            driveRecord = new DriveRecord
            {
                Id = Guid.NewGuid(),
                Path = drive.Path,
                Name = drive.Name,
                ModelFamily = smartaData.ModelFamily ?? string.Empty,
                DeviceModel = smartaData.DeviceModel ?? string.Empty,
                SerialNumber = serialKey,
                FirmwareVersion = smartaData.FirmwareVersion ?? string.Empty,
                FileSystem = drive.FileSystem,
                TotalSize = drive.TotalSize,
                FreeSpace = drive.FreeSpace,
                FirstSeen = testDate,
                LastSeen = testDate,
                TotalTests = 0
            };

            _dbContext.Drives.Add(driveRecord);
        }
        else
        {
            driveRecord.Path = drive.Path;
            driveRecord.Name = drive.Name;
            driveRecord.ModelFamily = smartaData.ModelFamily ?? driveRecord.ModelFamily;
            driveRecord.DeviceModel = smartaData.DeviceModel ?? driveRecord.DeviceModel;
            driveRecord.FirmwareVersion = smartaData.FirmwareVersion ?? driveRecord.FirmwareVersion;
            driveRecord.FileSystem = drive.FileSystem;
            driveRecord.TotalSize = drive.TotalSize;
            driveRecord.FreeSpace = drive.FreeSpace;
            driveRecord.LastSeen = testDate;
        }

        driveRecord.TotalTests += 1;

        var testRecord = new TestRecord
        {
            Id = Guid.NewGuid(),
            DriveId = driveRecord.Id,
            TestDate = testDate,
            TestType = SmartCheckTestType,
            AverageSpeed = 0,
            PeakSpeed = 0,
            MinSpeed = 0,
            TotalBytesWritten = 0,
            TotalBytesTested = 0,
            Errors = 0,
            Grade = rating.Grade.ToString(),
            Score = (int)rating.Score,
            CertificatePath = string.Empty,
            IsCompleted = true
        };

        var smartaRecord = new SmartaRecord
        {
            Id = Guid.NewGuid(),
            TestId = testRecord.Id,
            PowerOnHours = smartaData.PowerOnHours ?? 0,
            ReallocatedSectorCount = smartaData.ReallocatedSectorCount ?? 0,
            PendingSectorCount = smartaData.PendingSectorCount ?? 0,
            UncorrectableErrorCount = smartaData.UncorrectableErrorCount ?? 0,
            Temperature = smartaData.Temperature ?? 0,
            WearLevelingCount = smartaData.WearLevelingCount,
            Test = testRecord
        };

        testRecord.SmartaData = smartaRecord;
        driveRecord.Tests.Add(testRecord);

        _dbContext.Tests.Add(testRecord);
        _dbContext.SmartaData.Add(smartaRecord);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Also save to disk card for card view
        try
        {
            var card = await _cardTestService.GetOrCreateCardAsync(drive, smartaData, cancellationToken);
            await _cardTestService.SaveSmartCheckAsync(card, smartaData, rating, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log but don't fail - the legacy test record was saved
            LogDiskCardSaveFailed(_logger, ex);
        }

        return new SmartCheckResult
        {
            Drive = null,
            SmartaData = smartaData,
            Rating = rating,
            TestDate = testDate,
            TestId = testRecord.Id.ToString(),
            Attributes = attributes?.ToList() ?? new List<SmartaAttributeItem>(),
            SelfTestStatus = selfTestStatus,
            SelfTestLog = selfTestLog?.ToList() ?? new List<SmartaSelfTestEntry>(),
        };
    }

    public async Task<IReadOnlyList<SmartaMaintenanceAction>> GetSupportedMaintenanceActionsAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);
        if (AdvancedProvider == null)
        {
            return Array.Empty<SmartaMaintenanceAction>();
        }

        return await AdvancedProvider.GetSupportedMaintenanceActionsAsync(drive.Path, cancellationToken);
    }

    public async Task<bool> ExecuteMaintenanceActionAsync(CoreDriveInfo drive, SmartaMaintenanceAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);
        if (AdvancedProvider == null)
        {
            return false;
        }

        return await AdvancedProvider.ExecuteMaintenanceActionAsync(drive.Path, action, cancellationToken);
    }

    public async Task<IReadOnlyList<SmartaAttributeItem>> GetSmartAttributesAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);
        if (AdvancedProvider == null)
        {
            return Array.Empty<SmartaAttributeItem>();
        }

        return await AdvancedProvider.GetSmartAttributesAsync(drive.Path, cancellationToken);
    }

    public async Task<bool> StartSelfTestAsync(CoreDriveInfo drive, SmartaSelfTestType selfTestType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);
        if (AdvancedProvider == null)
        {
            return false;
        }

        return await AdvancedProvider.StartSelfTestAsync(drive.Path, selfTestType, cancellationToken);
    }

    public async Task<SmartaSelfTestStatus?> GetSelfTestStatusAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);
        if (AdvancedProvider == null)
        {
            return null;
        }

        return await AdvancedProvider.GetSelfTestStatusAsync(drive.Path, cancellationToken);
    }

    public async Task<IReadOnlyList<SmartaSelfTestEntry>> GetSelfTestLogAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);
        if (AdvancedProvider == null)
        {
            return Array.Empty<SmartaSelfTestEntry>();
        }

        return await AdvancedProvider.GetSelfTestLogAsync(drive.Path, cancellationToken);
    }

    public async Task<SmartaSelfTestReport> BuildSelfTestReportAsync(
        CoreDriveInfo drive,
        SmartaSelfTestType requestedType,
        DateTime startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);

        var status = await GetSelfTestStatusAsync(drive, cancellationToken);
        var log = await GetSelfTestLogAsync(drive, cancellationToken);
        var latestEntry = log.OrderByDescending(e => e.Number).FirstOrDefault();

        var summary = latestEntry == null
            ? "Self-test log is not available."
            : $"{latestEntry.TestType}: {latestEntry.Status}";

        var completed = status.HasValue && status.Value == SmartaSelfTestStatus.CompletedWithoutError || latestEntry != null;
        var passed = latestEntry != null
            && latestEntry.Status == SmartaSelfTestStatus.CompletedWithoutError
            || (latestEntry != null && (int)latestEntry.Status < 10);

        return new SmartaSelfTestReport
        {
            RequestedTestType = requestedType,
            Completed = completed,
            Passed = passed,
            Summary = summary,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = DateTime.UtcNow,
            RecentEntries = log.Take(10).ToList()
        };
    }

    public async Task<string?> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default)
    {
        return await _smartaProvider.GetDependencyInstructionsAsync(cancellationToken);
    }

    public async Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default)
    {
        return await _smartaProvider.TryInstallDependenciesAsync(cancellationToken);
    }

    public async Task<SmartaData?> GetSmartaDataSnapshotAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);
        return await _smartaProvider.GetSmartaDataAsync(drive.Path, cancellationToken);
    }

    public async Task<SmartaData?> GetSmartaDataWithRetryAsync(CoreDriveInfo drive, int maxRetries = 3, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var data = await _smartaProvider.GetSmartaDataAsync(drive.Path, cancellationToken);
                if (data != null)
                {
                    return data;
                }
            }
            catch
            {
                // Silently retry on any error
            }

            if (attempt < maxRetries - 1)
            {
                await Task.Delay((int)(500 * Math.Pow(2, attempt)), cancellationToken);
            }
        }

        return null;
    }

    public async Task<int?> GetTemperatureOnlyAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);

        try
        {
            return await _smartaProvider.GetTemperatureOnlyAsync(drive.Path, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
