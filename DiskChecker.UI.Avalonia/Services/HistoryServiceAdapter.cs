using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.Core.Models;
using System.Linq;
using DiskChecker.Core.Interfaces;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Adapter that exposes history operations through the UI-facing IHistoryService interface.
/// </summary>
internal class HistoryServiceAdapter : IHistoryService
{
    private readonly DiskChecker.Application.Services.HistoryService _legacyHistoryService;
    private readonly DiskChecker.Application.Services.TestHistoryService _testHistoryService;
    private readonly IDiskCardRepository _diskCardRepository;

    public HistoryServiceAdapter(
        DiskChecker.Application.Services.HistoryService legacyHistoryService,
        DiskChecker.Application.Services.TestHistoryService testHistoryService,
        IDiskCardRepository diskCardRepository)
    {
        _legacyHistoryService = legacyHistoryService ?? throw new ArgumentNullException(nameof(legacyHistoryService));
        _testHistoryService = testHistoryService ?? throw new ArgumentNullException(nameof(testHistoryService));
        _diskCardRepository = diskCardRepository ?? throw new ArgumentNullException(nameof(diskCardRepository));
    }

    public async Task<IEnumerable<HistoricalTest>> GetHistoryAsync()
    {
        var reports = await _testHistoryService.GetAllTestReportsAsync();
        if (reports.Count > 0)
        {
            return reports.Select(MapToHistoricalTest);
        }

        var legacy = await _legacyHistoryService.GetHistoryAsync(CancellationToken.None);
        return legacy;
    }

    public async Task<IEnumerable<HistoricalTest>> GetHistoryForDiskAsync(string serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            return Enumerable.Empty<HistoricalTest>();
        }

        var reports = await _testHistoryService.GetReportsForDiskAsync(serialNumber);
        if (reports.Count > 0)
        {
            return reports.Select(MapToHistoricalTest);
        }

        var legacy = await _legacyHistoryService.GetHistoryForDiskAsync(serialNumber, CancellationToken.None);
        return legacy;
    }

    public async Task DeleteHistoryAsync(Guid testId)
    {
        var cards = await _diskCardRepository.GetAllAsync();
        foreach (var card in cards)
        {
            var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
            var session = sessions.FirstOrDefault(s => s.SessionId == testId);
            if (session != null)
            {
                await _diskCardRepository.DeleteTestSessionAsync(session.Id);
                return;
            }
        }

        await _legacyHistoryService.DeleteHistoryAsync(testId, CancellationToken.None);
    }

    public async Task ClearHistoryAsync()
    {
        var cards = await _diskCardRepository.GetAllAsync();
        foreach (var card in cards)
        {
            var sessions = await _diskCardRepository.GetTestSessionsAsync(card.Id);
            foreach (var session in sessions)
            {
                await _diskCardRepository.DeleteTestSessionAsync(session.Id);
            }
        }

        await _legacyHistoryService.ClearHistoryAsync(CancellationToken.None);
    }

    /// <summary>
    /// Maps report data to history model consumed by the UI.
    /// </summary>
    private static HistoricalTest MapToHistoricalTest(TestReport report)
    {
        return new HistoricalTest
        {
            Id = report.ReportId,
            SerialNumber = report.SerialNumber,
            Model = report.DriveModel,
            TestDate = report.TestDate,
            TestType = report.TestType,
            Grade = report.Grade,
            Score = report.Score,
            ErrorCount = report.Errors,
            AverageThroughputMbps = report.AverageSpeed,
            PeakThroughputMbps = report.PeakSpeed,
            TotalBytesTested = 0,
            HealthAssessment = report.IsCompleted ? "Completed" : "Incomplete",
            Duration = 0,
            Notes = null
        };
    }
}
