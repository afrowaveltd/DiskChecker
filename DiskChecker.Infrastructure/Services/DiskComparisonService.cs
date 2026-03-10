using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Infrastructure.Services;

/// <summary>
/// Service for comparing disks and selecting the best one.
/// </summary>
public class DiskComparisonService : IDiskComparisonService
{
    private readonly IDiskCardRepository _repository;
    private readonly ILogger<DiskComparisonService>? _logger;

    public DiskComparisonService(IDiskCardRepository repository, ILogger<DiskComparisonService>? logger = null)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<DiskComparisonResult>> CompareAsync(List<int> diskCardIds)
    {
        var results = new List<DiskComparisonResult>();
        
        var cards = new List<DiskCard>();
        foreach (var id in diskCardIds)
        {
            var card = await _repository.GetByIdAsync(id);
            if (card != null && !card.IsArchived)
            {
                cards.Add(card);
            }
        }

        if (cards.Count == 0)
        {
            return results;
        }

        // Get latest test session for each card
        var sessionTasks = cards.Select(async card =>
        {
            var sessions = await _repository.GetTestSessionsAsync(card.Id);
            return (Card: card, LatestSession: sessions.FirstOrDefault());
        });

        var sessions = await Task.WhenAll(sessionTasks);

        // Calculate scores and rank
        var ranked = sessions
            .Where(s => s.LatestSession != null)
            .Select(s =>
            {
                var session = s.LatestSession!;
                var recommendation = GetRecommendation(s.Card, session);

                return new DiskComparisonResult
                {
                    Disk = s.Card,
                    Rank = 0, // Will be set below
                    Score = session.Score,
                    Grade = session.Grade,
                    AvgWriteSpeed = session.AverageWriteSpeedMBps,
                    AvgReadSpeed = session.AverageReadSpeedMBps,
                    ErrorCount = session.Errors.Count + session.VerificationErrors,
                    Recommendation = recommendation
                };
            })
            .OrderByDescending(r => r.Score)
            .ToList();

        // Assign ranks
        for (int i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;
        }

        // Add cards without tests at the end
        var withoutTests = sessions
            .Where(s => s.LatestSession == null)
            .Select(s => new DiskComparisonResult
            {
                Disk = s.Card,
                Rank = ranked.Count + 1,
                Score = 0,
                Grade = "?",
                AvgWriteSpeed = 0,
                AvgReadSpeed = 0,
                ErrorCount = 0,
                Recommendation = "Disk nebyl testován"
            });

        results.AddRange(ranked);
        results.AddRange(withoutTests);

        return results;
    }

    public async Task<DiskCard?> GetBestDiskAsync(List<int> diskCardIds)
    {
        var results = await CompareAsync(diskCardIds);
        return results.FirstOrDefault()?.Disk;
    }

    public async Task<List<DiskCard>> GetRecommendedDisksAsync(string useCase, int count = 5)
    {
        var allCards = await _repository.GetActiveAsync();
        
        var recommended = allCards
            .Where(c => c.TestCount > 0)
            .OrderByDescending(c => c.OverallScore);

        // Filter by use case
        var filtered = useCase.ToLowerInvariant() switch
        {
            "server" => recommended.Where(c => c.OverallScore >= 80),
            "workstation" => recommended.Where(c => c.OverallScore >= 70),
            "archive" => recommended.Where(c => c.OverallScore >= 60),
            "testing" => recommended,
            _ => recommended
        };

        return filtered.Take(count).ToList();
    }

    public async Task<PerformanceComparison> ComparePerformanceAsync(int diskCardId1, int diskCardId2)
    {
        var card1 = await _repository.GetByIdAsync(diskCardId1);
        var card2 = await _repository.GetByIdAsync(diskCardId2);

        if (card1 == null || card2 == null)
        {
            throw new ArgumentException("One or both disks not found");
        }

        var sessions1 = await _repository.GetTestSessionsAsync(diskCardId1);
        var sessions2 = await _repository.GetTestSessionsAsync(diskCardId2);

        var latest1 = sessions1.FirstOrDefault();
        var latest2 = sessions2.FirstOrDefault();

        if (latest1 == null || latest2 == null)
        {
            throw new ArgumentException("One or both disks have no test sessions");
        }

        var writeDiff = latest1.AverageWriteSpeedMBps - latest2.AverageWriteSpeedMBps;
        var readDiff = latest1.AverageReadSpeedMBps - latest2.AverageReadSpeedMBps;

        var fasterDisk = Math.Abs(writeDiff) > Math.Abs(readDiff)
            ? (writeDiff > 0 ? card1.ModelName : card2.ModelName)
            : (readDiff > 0 ? card1.ModelName : card2.ModelName);

        var speedAdvantage = (int)((Math.Max(
            Math.Abs(writeDiff) / latest2.AverageWriteSpeedMBps,
            Math.Abs(readDiff) / latest2.AverageReadSpeedMBps) * 100));

        var score1 = latest1.Score;
        var score2 = latest2.Score;

        var moreReliable = score1 > score2 ? card1.ModelName : card2.ModelName;
        var recommended = score1 > score2 ? card1.ModelName : card2.ModelName;

        var summary = score1 > score2 + 10
            ? $"{card1.ModelName} je výrazně lepší než {card2.ModelName}. Doporučujeme použít tento disk."
            : score2 > score1 + 10
            ? $"{card2.ModelName} je výrazně lepší než {card1.ModelName}. Doporučujeme použít tento disk."
            : "Oba disky mají srovnatelný výkon. Volba závisí na specifických potřebách.";

        return new PerformanceComparison
        {
            Disk1 = card1,
            Disk2 = card2,
            WriteSpeedDifference = writeDiff,
            ReadSpeedDifference = readDiff,
            SpeedAdvantagePercent = speedAdvantage,
            FasterDisk = fasterDisk,
            MoreReliableDisk = moreReliable,
            RecommendedDisk = recommended,
            Summary = summary
        };
    }

    private static string GetRecommendation(DiskCard card, TestSession session)
    {
        if (session.Result == TestResult.Fail)
        {
            return "⚠️ Neprošel testem - nedoporučeno pro použití";
        }

        if (session.Errors.Count > 10 || session.VerificationErrors > 5)
        {
            return "⚠️ Vysoce chybové - pouze pro nekritické použití";
        }

        if (session.Score >= 90)
        {
            return "✅ Výborný stav - doporučeno pro všechny účely";
        }

        if (session.Score >= 70)
        {
            return "✅ Dobrý stav - vhodný pro běžné použití";
        }

        if (session.Score >= 50)
        {
            return "⚠️ Slabší výkon - vhodný pro méně náročné úlohy";
        }

        return "❌ Špatný stav - vyžaduje pozornost";
    }
}