using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Application.Services;

/// <summary>
/// Orchestrates seek tests: gets SMART data, obtains recommendation,
/// executes the test via ISeekTestExecutor, and persists results.
/// </summary>
public class SeekTestService
{
    private const string SeekTestTypePrefix = "Seek";

    private static readonly Action<ILogger, string, Exception?> LogSmartUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(SeekTestService)),
            "Could not retrieve SMART data for seek recommendation on {DrivePath}.");

    private static readonly Action<ILogger, string, Exception?> LogSmartBeforeUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(SeekTestService)),
            "Could not retrieve SMART data before seek test on {DrivePath}.");

    private static readonly Action<ILogger, string, Exception?> LogTooFragile =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3, nameof(SeekTestService)),
            "Seek test refused: disk {DrivePath} is too fragile for seek testing.");

    private readonly ISmartaProvider _smartaProvider;
    private readonly ISeekTestExecutor _seekExecutor;
    private readonly ILogger<SeekTestService> _logger;

    private IAdvancedSmartaProvider? AdvancedProvider => _smartaProvider as IAdvancedSmartaProvider;

    public SeekTestService(
        ISmartaProvider smartaProvider,
        ISeekTestExecutor seekExecutor,
        ILogger<SeekTestService> logger)
    {
        _smartaProvider = smartaProvider;
        _seekExecutor = seekExecutor;
        _logger = logger;
    }

    /// <summary>
    /// Gets a SMART-informed recommendation for seek test parameters.
    /// Skips SMART query when the drive does not support SMART (SupportsSmart == false)
    /// to avoid device contention (e.g., "device is busy" on Linux).
    /// </summary>
    public async Task<SeekTestRecommendation> GetRecommendationAsync(
        CoreDriveInfo drive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);

        SmartaData? smartaData = null;
        if (drive.SupportsSmart)
        {
            try
            {
                smartaData = await _smartaProvider.GetSmartaDataAsync(drive.Path, cancellationToken);
            }
            catch (Exception ex)
            {
                LogSmartUnavailable(_logger, drive.Path, ex);
            }
        }
        else
        {
            LogSmartUnavailable(_logger, drive.Path, null);
        }

        var isSolidState = IsSolidStateDrive(smartaData, drive);
        return _seekExecutor.GetRecommendation(smartaData, drive.TotalSize, isSolidState);
    }

    /// <summary>
    /// Runs a seek test with the given request configuration.
    /// </summary>
    public async Task<SeekTestResult> RunAsync(
        SeekTestRequest request,
        Action<SeekTestProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Drive);

        // Get SMART data for the result metadata (only if drive supports SMART)
        SmartaData? smartaData = null;
        if (request.Drive.SupportsSmart)
        {
            try
            {
                smartaData = await _smartaProvider.GetSmartaDataAsync(request.Drive.Path, cancellationToken);
            }
            catch (Exception ex)
            {
                LogSmartBeforeUnavailable(_logger, request.Drive.Path, ex);
            }
        }
        else
        {
            LogSmartBeforeUnavailable(_logger, request.Drive.Path, null);
        }

        // Get recommendation for result metadata
        var isSolidState = IsSolidStateDrive(smartaData, request.Drive);
        var recommendation = _seekExecutor.GetRecommendation(smartaData, request.Drive.TotalSize, isSolidState);

        // Execute the seek test
        var result = await _seekExecutor.ExecuteAsync(request, progressCallback, cancellationToken);

        // Enrich result with drive metadata
        result.DriveModel = smartaData?.DeviceModel ?? request.Drive.Name;
        result.DriveSerialNumber = smartaData?.SerialNumber ?? request.Drive.SerialNumber;
        result.DrivePath = request.Drive.Path;
        result.DriveTotalBytes = request.Drive.TotalSize;
        result.PowerOnHours = smartaData?.PowerOnHours;
        result.Recommendation = recommendation;

        return result;
    }

    /// <summary>
    /// Runs a seek test using the SMART-informed recommendation automatically.
    /// </summary>
    public async Task<SeekTestResult> RunWithRecommendationAsync(
        CoreDriveInfo drive,
        SeekTestType? preferredType = null,
        Action<SeekTestProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(drive);

        var recommendation = await GetRecommendationAsync(drive, cancellationToken);

        if (recommendation.IsTooFragile)
        {
            LogTooFragile(_logger, drive.Path, null);
            return new SeekTestResult
            {
                DriveModel = drive.Name,
                DrivePath = drive.Path,
                DriveTotalBytes = drive.TotalSize,
                IsCompleted = false,
                WasAborted = true,
                Notes = $"Seek test skipped: {recommendation.Rationale}",
                Recommendation = recommendation
            };
        }

        var testType = preferredType ?? recommendation.RecommendedType;

        var request = new SeekTestRequest
        {
            Drive = drive,
            TestType = testType,
            SeekCount = recommendation.RecommendedSeekCount,
            SkipSegments = recommendation.RecommendedSkipSegments,
            CollectLatencySamples = true,
            TimeoutSeconds = Math.Max(60, recommendation.RecommendedSeekCount / 10 + 30)
        };

        return await RunAsync(request, progressCallback, cancellationToken);
    }

    /// <summary>
    /// Checks whether seek testing is supported on the current platform.
    /// </summary>
    public async Task<bool> IsPlatformSupportedAsync(CancellationToken cancellationToken = default)
    {
        return await _seekExecutor.IsPlatformSupportedAsync(cancellationToken);
    }

    private static bool IsSolidStateDrive(SmartaData? smartaData, CoreDriveInfo drive)
    {
        if (smartaData != null)
        {
            // NVMe drives are always solid state
            if (smartaData.DeviceType?.Contains("nvme", StringComparison.OrdinalIgnoreCase) == true)
                return true;

            // Check for SSD-specific SMART attributes
            if (smartaData.WearLevelingCount.HasValue || smartaData.PercentageUsed.HasValue)
                return true;

            // Check device model for SSD indicators
            var model = smartaData.DeviceModel ?? "";
            if (model.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                model.Contains("Solid State", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check drive path for NVMe
        if (drive.Path?.Contains("nvme", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        // Check drive name
        if (drive.Name?.Contains("SSD", StringComparison.OrdinalIgnoreCase) == true ||
            drive.Name?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return false;
    }
}
