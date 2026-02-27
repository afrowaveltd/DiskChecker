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
    private readonly ISmartaProvider _smartaProvider;
    private readonly IQualityCalculator _qualityCalculator;
    private readonly DiskCheckerDbContext _dbContext;
    private readonly ILogger<SmartCheckService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmartCheckService"/> class.
    /// </summary>
    /// <param name="smartaProvider">Provider used to read SMART data.</param>
    /// <param name="qualityCalculator">Calculator used to rate SMART data.</param>
    /// <param name="dbContext">Database context for persistence.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public SmartCheckService(
        ISmartaProvider smartaProvider,
        IQualityCalculator qualityCalculator,
        DiskCheckerDbContext dbContext,
        ILogger<SmartCheckService> logger)
    {
        _smartaProvider = smartaProvider;
        _qualityCalculator = qualityCalculator;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Runs a SMART check for the selected drive and persists the results.
    /// </summary>
    /// <param name="drive">Drive to check.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The SMART check result or <c>null</c> when data cannot be collected.</returns>
    public async Task<SmartCheckResult?> RunAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);

        var smartaData = await _smartaProvider.GetSmartaDataAsync(drive.Path, cancellationToken);
        if (smartaData == null)
        {
            LogSmartDataUnavailable(_logger, drive.Path, null);
            return null;
        }

        var rating = _qualityCalculator.CalculateQuality(smartaData);
        var testDate = DateTime.UtcNow;
        smartaData.LastChecked = testDate;

        var serialKey = (string.IsNullOrWhiteSpace(smartaData.SerialNumber) ? drive.Path : smartaData.SerialNumber).Trim();
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
            Errors = 0,
            Grade = rating.Grade,
            Score = rating.Score,
            CertificatePath = string.Empty
        };

        var smartaRecord = new SmartaRecord
        {
            Id = Guid.NewGuid(),
            TestId = testRecord.Id,
            PowerOnHours = smartaData.PowerOnHours,
            ReallocatedSectorCount = smartaData.ReallocatedSectorCount,
            PendingSectorCount = smartaData.PendingSectorCount,
            UncorrectableErrorCount = smartaData.UncorrectableErrorCount,
            Temperature = smartaData.Temperature,
            WearLevelingCount = smartaData.WearLevelingCount,
            Test = testRecord
        };

        testRecord.SmartaData = smartaRecord;
        driveRecord.Tests.Add(testRecord);

        _dbContext.Tests.Add(testRecord);
        _dbContext.SmartaData.Add(smartaRecord);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SmartCheckResult
        {
            Drive = drive,
            SmartaData = smartaData,
            Rating = rating,
            TestDate = testDate,
            TestId = testRecord.Id
        };
    }

    /// <summary>
    /// Gets instructions on how to install missing system dependencies.
    /// </summary>
    public async Task<string?> GetDependencyInstructionsAsync(CancellationToken cancellationToken = default)
    {
        return await _smartaProvider.GetDependencyInstructionsAsync(cancellationToken);
    }

    /// <summary>
    /// Attempts to automatically install missing system dependencies.
    /// </summary>
    public async Task<bool> TryInstallDependenciesAsync(CancellationToken cancellationToken = default)
    {
        return await _smartaProvider.TryInstallDependenciesAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a SMART data snapshot without persisting it to the database.
    /// </summary>
    /// <param name="drive">Drive to read.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>SMART data snapshot or <c>null</c> when unavailable.</returns>
    public async Task<SmartaData?> GetSmartaDataSnapshotAsync(CoreDriveInfo drive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);
        return await _smartaProvider.GetSmartaDataAsync(drive.Path, cancellationToken);
    }

    /// <summary>
    /// Reads SMART data snapshot with retry logic for more reliable updates during testing.
    /// </summary>
    /// <param name="drive">Drive to read.</param>
    /// <param name="maxRetries">Maximum retry attempts.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>SMART data snapshot or <c>null</c> when unavailable after retries.</returns>
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

    /// <summary>
    /// Gets ONLY the temperature (fast, works even during disk operations).
    /// This is useful for live temperature updates during surface tests.
    /// </summary>
    /// <param name="drive">Drive to check.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Temperature in Celsius or <c>null</c> when unavailable.</returns>
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
