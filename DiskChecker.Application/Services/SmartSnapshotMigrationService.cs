using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Application.Services;

/// <summary>
/// Migrates existing TestSession SMART JSON data into the SmartSnapshots table
/// for historical trend analysis. Safe to run multiple times (idempotent).
/// </summary>
public class SmartSnapshotMigrationService
{
    private static readonly Action<ILogger, int, string, Exception?> LogBackfillCreated =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(1, nameof(SmartSnapshotMigrationService)),
            "Backfilled {Count} SMART snapshots for disk card {CardId}");

    private static readonly Action<ILogger, int, Exception?> LogBackfillFailed =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(2, nameof(SmartSnapshotMigrationService)),
            "Failed to backfill SMART snapshots for disk card {CardId}");

    private readonly IDiskCardRepository _repository;
    private readonly SmartTrendService _trendService;
    private readonly ILogger<SmartSnapshotMigrationService>? _logger;

    public SmartSnapshotMigrationService(
        IDiskCardRepository repository,
        SmartTrendService trendService,
        ILogger<SmartSnapshotMigrationService>? logger = null)
    {
        _repository = repository;
        _trendService = trendService;
        _logger = logger;
    }

    /// <summary>
    /// Backfills SmartSnapshotRecords for all disks from existing TestSession data.
    /// Returns total number of snapshots created.
    /// </summary>
    public async Task<int> BackfillAllDisksAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var cards = await _repository.GetAllAsync();
        var totalCreated = 0;

        foreach (var card in cards)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var created = await _trendService.BackfillFromTestSessionsAsync(card.Id);
                totalCreated += created;
                if (created > 0 && _logger != null)
                {
                    LogBackfillCreated(_logger, created, card.ModelName, null);
                }
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    LogBackfillFailed(_logger, card.Id, ex);
                }
            }
        }

        return totalCreated;
    }
}
