using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Aggregates historical SMART data and computes trends for key metrics.
/// Provides vendor-specific wear assessment and chart data for visualization.
/// </summary>
public class SmartTrendService
{
    private readonly IDiskCardRepository _repository;

    public SmartTrendService(IDiskCardRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Builds a complete trend report for a disk from historical SMART snapshots.
    /// </summary>
    public async Task<SmartTrendReport> BuildTrendReportAsync(int diskCardId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var card = await _repository.GetByIdAsync(diskCardId);
        var snapshots = await _repository.GetSmartSnapshotsAsync(diskCardId);

        var report = new SmartTrendReport
        {
            DiskCardId = diskCardId,
            DiskModel = card?.ModelName ?? "Neznámý",
            SerialNumber = card?.SerialNumber ?? "",
            SnapshotCount = snapshots.Count,
            FirstSnapshot = snapshots.FirstOrDefault()?.RetrievedAtUtc,
            LastSnapshot = snapshots.LastOrDefault()?.RetrievedAtUtc
        };

        if (snapshots.Count < 2)
        {
            // Not enough data for trends, but still provide wear assessment
            if (snapshots.Count == 1)
            {
                var latest = snapshots[0];
                report.WearAssessment = ComputeWearAssessment(latest, card?.ModelName);
            }
            report.Summary = snapshots.Count == 0
                ? "Žádná historická SMART data. Trendy budou dostupné po několika testech."
                : "Pouze jeden SMART snapshot. Pro trendy jsou potřeba alespoň 2 měření.";
            return report;
        }

        // Compute trends for key metrics
        report.Trends.Add(ComputeTrend("Teplota", "°C", snapshots, s => s.Temperature, higherIsWorse: true, warningThreshold: 55, criticalThreshold: 65));
        report.Trends.Add(ComputeTrend("Power-On Hours", "h", snapshots, s => s.PowerOnHours, higherIsWorse: false));
        report.Trends.Add(ComputeTrend("Reallocated Sectors", "", snapshots, s => s.ReallocatedSectorCount, higherIsWorse: true, warningThreshold: 5, criticalThreshold: 20));
        report.Trends.Add(ComputeTrend("Pending Sectors", "", snapshots, s => s.PendingSectorCount, higherIsWorse: true, warningThreshold: 1, criticalThreshold: 5));
        report.Trends.Add(ComputeTrend("Uncorrectable Errors", "", snapshots, s => s.UncorrectableErrorCount, higherIsWorse: true, warningThreshold: 1, criticalThreshold: 3));
        report.Trends.Add(ComputeTrend("Media Errors", "", snapshots, s => s.MediaErrors, higherIsWorse: true, warningThreshold: 1, criticalThreshold: 10));
        report.Trends.Add(ComputeTrend("Percentage Used", "%", snapshots, s => s.PercentageUsed, higherIsWorse: true, warningThreshold: 50, criticalThreshold: 80));
        report.Trends.Add(ComputeTrend("Available Spare", "%", snapshots, s => s.AvailableSpare, higherIsWorse: false, warningThreshold: 20, criticalThreshold: 10));
        report.Trends.Add(ComputeTrend("Wear Leveling", "%", snapshots, s => s.WearLevelingCount, higherIsWorse: false, warningThreshold: 30, criticalThreshold: 10));
        report.Trends.Add(ComputeTrend("Unsafe Shutdowns", "", snapshots, s => s.UnsafeShutdowns, higherIsWorse: true, warningThreshold: 100, criticalThreshold: 500));

        // Wear assessment from latest snapshot
        var latestSnapshot = snapshots.Last();
        report.WearAssessment = ComputeWearAssessment(latestSnapshot, card?.ModelName);

        // Build summary
        var degradingTrends = report.Trends.Where(t => t.IsDegrading).ToList();
        if (degradingTrends.Count > 0)
        {
            var criticalCount = degradingTrends.Count(t => t.DaysUntilCritical < 90);
            var warningCount = degradingTrends.Count(t => t.DaysUntilCritical < 365);
            report.Summary = criticalCount > 0
                ? $"⚠️ {criticalCount} kritických trendů degradace, {warningCount} varovných. " +
                  string.Join("; ", degradingTrends.Where(t => t.DaysUntilCritical < 90).Select(t => $"{t.MetricName}: {t.DaysUntilCritical:F0} dní do kritické meze"))
                : $"📊 {warningCount} trendů vykazuje pozvolnou degradaci. " +
                  string.Join("; ", degradingTrends.Where(t => t.DaysUntilCritical < 365).Select(t => $"{t.MetricName}: +{t.RatePerDay:F3}/den"));
        }
        else
        {
            report.Summary = "✅ Všechny SMART metriky jsou stabilní bez známek degradace.";
        }

        return report;
    }

    /// <summary>
    /// Generates chart data for a specific metric trend, suitable for SVG polyline rendering.
    /// </summary>
    public SmartTrendChartData BuildChartData(SmartMetricTrend trend, double width = 520, double height = 180)
    {
        var validPoints = trend.Points
            .Where(p => p.Value.HasValue)
            .OrderBy(p => p.Timestamp)
            .ToList();

        if (validPoints.Count == 0)
        {
            return new SmartTrendChartData
            {
                Title = trend.MetricName,
                YAxisLabel = trend.Unit,
                Unit = trend.Unit,
                PolylinePoints = string.Empty
            };
        }

        var minVal = validPoints.Min(p => p.Value!.Value);
        var maxVal = validPoints.Max(p => p.Value!.Value);
        var range = Math.Max(1, maxVal - minVal);

        // Add 10% padding
        var paddedMin = minVal - range * 0.1;
        var paddedMax = maxVal + range * 0.1;
        var paddedRange = Math.Max(1, paddedMax - paddedMin);

        var points = validPoints.Select((p, i) =>
        {
            var x = validPoints.Count == 1 ? width / 2 : i / (double)(validPoints.Count - 1) * width;
            var y = height - ((p.Value!.Value - paddedMin) / paddedRange * height);
            return $"{x:F1},{y:F1}";
        });

        return new SmartTrendChartData
        {
            Title = trend.MetricName,
            YAxisLabel = trend.Unit,
            Unit = trend.Unit,
            Points = validPoints,
            MinValue = paddedMin,
            MaxValue = paddedMax,
            PolylinePoints = string.Join(' ', points)
        };
    }

    /// <summary>
    /// Converts a SmartaData snapshot to a SmartSnapshotRecord and persists it.
    /// Should be called whenever SMART data is collected (before/after test).
    /// </summary>
    public async Task<SmartSnapshotRecord> PersistSnapshotAsync(int diskCardId, SmartaData smartData, int? testSessionId = null)
    {
        var record = new SmartSnapshotRecord
        {
            DiskCardId = diskCardId,
            TestSessionId = testSessionId,
            RetrievedAtUtc = smartData.RetrievedAtUtc ?? DateTime.UtcNow,
            IsHealthy = smartData.IsHealthy,
            IsFailing = smartData.IsFailing,
            Temperature = smartData.Temperature,
            PowerOnHours = smartData.PowerOnHours,
            PowerCycleCount = smartData.PowerCycleCount,
            ReallocatedSectorCount = smartData.ReallocatedSectorCount,
            PendingSectorCount = smartData.PendingSectorCount,
            UncorrectableErrorCount = smartData.UncorrectableErrorCount,
            WearLevelingCount = smartData.WearLevelingCount,
            AvailableSpare = smartData.AvailableSpare,
            PercentageUsed = smartData.PercentageUsed,
            MediaErrors = smartData.MediaErrors,
            UnsafeShutdowns = smartData.UnsafeShutdowns
        };

        return await _repository.CreateSmartSnapshotAsync(record);
    }

    /// <summary>
    /// Backfills SmartSnapshotRecords from existing TestSession JSON data.
    /// Useful for migration/upgrade scenarios.
    /// </summary>
    public async Task<int> BackfillFromTestSessionsAsync(int diskCardId)
    {
        var sessions = await _repository.GetTestSessionsAsync(diskCardId);
        var existingSnapshots = await _repository.GetSmartSnapshotsAsync(diskCardId);
        var existingTestSessionIds = existingSnapshots
            .Where(s => s.TestSessionId.HasValue)
            .Select(s => s.TestSessionId!.Value)
            .ToHashSet();

        var count = 0;
        foreach (var session in sessions.OrderBy(s => s.StartedAt))
        {
            if (existingTestSessionIds.Contains(session.Id))
                continue;

            // Persist SmartBefore if available
            if (session.SmartBefore != null)
            {
                var record = new SmartSnapshotRecord
                {
                    DiskCardId = diskCardId,
                    TestSessionId = session.Id,
                    RetrievedAtUtc = session.SmartBefore.RetrievedAtUtc ?? session.StartedAt,
                    IsHealthy = session.SmartBefore.IsHealthy,
                    IsFailing = session.SmartBefore.IsFailing,
                    Temperature = session.SmartBefore.Temperature,
                    PowerOnHours = session.SmartBefore.PowerOnHours,
                    PowerCycleCount = session.SmartBefore.PowerCycleCount,
                    ReallocatedSectorCount = session.SmartBefore.ReallocatedSectorCount,
                    PendingSectorCount = session.SmartBefore.PendingSectorCount,
                    UncorrectableErrorCount = session.SmartBefore.UncorrectableErrorCount,
                    WearLevelingCount = session.SmartBefore.WearLevelingCount,
                    AvailableSpare = session.SmartBefore.AvailableSpare,
                    PercentageUsed = session.SmartBefore.PercentageUsed,
                    MediaErrors = session.SmartBefore.MediaErrors,
                    UnsafeShutdowns = session.SmartBefore.UnsafeShutdowns
                };
                await _repository.CreateSmartSnapshotAsync(record);
                count++;
            }

            // Also persist SmartAfter if different from SmartBefore
            if (session.SmartAfter != null && session.SmartBefore != null)
            {
                // Only add if there are meaningful differences
                if (HasMeaningfulDifferences(session.SmartBefore, session.SmartAfter))
                {
                    var record = new SmartSnapshotRecord
                    {
                        DiskCardId = diskCardId,
                        TestSessionId = session.Id,
                        RetrievedAtUtc = session.SmartAfter.RetrievedAtUtc ?? session.CompletedAt ?? session.StartedAt,
                        IsHealthy = session.SmartAfter.IsHealthy,
                        IsFailing = session.SmartAfter.IsFailing,
                        Temperature = session.SmartAfter.Temperature,
                        PowerOnHours = session.SmartAfter.PowerOnHours,
                        PowerCycleCount = session.SmartAfter.PowerCycleCount,
                        ReallocatedSectorCount = session.SmartAfter.ReallocatedSectorCount,
                        PendingSectorCount = session.SmartAfter.PendingSectorCount,
                        UncorrectableErrorCount = session.SmartAfter.UncorrectableErrorCount,
                        WearLevelingCount = session.SmartAfter.WearLevelingCount,
                        AvailableSpare = session.SmartAfter.AvailableSpare,
                        PercentageUsed = session.SmartAfter.PercentageUsed,
                        MediaErrors = session.SmartAfter.MediaErrors,
                        UnsafeShutdowns = session.SmartAfter.UnsafeShutdowns
                    };
                    await _repository.CreateSmartSnapshotAsync(record);
                    count++;
                }
            }
        }

        return count;
    }

    private static bool HasMeaningfulDifferences(SmartaData before, SmartaData after)
    {
        return before.Temperature != after.Temperature ||
               before.PowerOnHours != after.PowerOnHours ||
               before.ReallocatedSectorCount != after.ReallocatedSectorCount ||
               before.PendingSectorCount != after.PendingSectorCount ||
               before.UncorrectableErrorCount != after.UncorrectableErrorCount ||
               before.MediaErrors != after.MediaErrors ||
               before.PercentageUsed != after.PercentageUsed ||
               before.WearLevelingCount != after.WearLevelingCount;
    }

    private static SmartMetricTrend ComputeTrend(
        string name,
        string unit,
        List<SmartSnapshotRecord> snapshots,
        Func<SmartSnapshotRecord, int?> valueSelector,
        bool higherIsWorse,
        double? warningThreshold = null,
        double? criticalThreshold = null)
    {
        var trend = new SmartMetricTrend
        {
            MetricName = name,
            Unit = unit
        };

        var validSnapshots = snapshots
            .Where(s => valueSelector(s).HasValue)
            .OrderBy(s => s.RetrievedAtUtc)
            .ToList();

        if (validSnapshots.Count < 2)
        {
            if (validSnapshots.Count == 1)
            {
                trend.Points.Add(new SmartTrendPoint
                {
                    Timestamp = validSnapshots[0].RetrievedAtUtc,
                    Value = valueSelector(validSnapshots[0])
                });
            }
            trend.Description = "Nedostatek dat pro výpočet trendu.";
            return trend;
        }

        // Build points
        foreach (var snap in validSnapshots)
        {
            trend.Points.Add(new SmartTrendPoint
            {
                Timestamp = snap.RetrievedAtUtc,
                Value = valueSelector(snap)
            });
        }

        // Calculate rate of change (linear regression)
        var first = validSnapshots.First();
        var last = validSnapshots.Last();
        var firstVal = valueSelector(first)!.Value;
        var lastVal = valueSelector(last)!.Value;
        var totalHours = (last.RetrievedAtUtc - first.RetrievedAtUtc).TotalHours;

        if (totalHours > 0)
        {
            var rawDelta = lastVal - firstVal;
            // For metrics where lower is better (e.g., AvailableSpare, WearLeveling), negate the delta
            var effectiveDelta = higherIsWorse ? rawDelta : -rawDelta;
            trend.RatePerDay = effectiveDelta / totalHours * 24;

            // Project days until critical threshold
            if (trend.RatePerDay > 0 && criticalThreshold.HasValue)
            {
                var distance = higherIsWorse
                    ? criticalThreshold.Value - lastVal
                    : lastVal - criticalThreshold.Value;
                if (distance > 0 && trend.RatePerDay > 0)
                {
                    trend.DaysUntilCritical = distance / trend.RatePerDay.Value;
                }
                else if (distance <= 0)
                {
                    trend.DaysUntilCritical = 0; // Already at or past critical
                }
            }

            // Build description
            var parts = new List<string>();
            if (Math.Abs(rawDelta) > 0)
            {
                parts.Add($"Δ {rawDelta:+#;-#;0}{unit} za {totalHours / 24:F1} dní");
            }
            if (trend.RatePerDay.HasValue && Math.Abs(trend.RatePerDay.Value) > 0.001)
            {
                parts.Add($"{(higherIsWorse ? "+" : "")}{trend.RatePerDay.Value:+0.000;-0.000;0}{unit}/den");
            }
            if (trend.DaysUntilCritical.HasValue)
            {
                parts.Add(trend.DaysUntilCritical.Value <= 0
                    ? "⚠ KRITICKÁ MEZ"
                    : $"⏱ {trend.DaysUntilCritical.Value:F0} dní do kritické meze");
            }

            trend.Description = string.Join(" | ", parts);
        }
        else
        {
            trend.Description = "Stejný časový údaj – nelze vypočítat rychlost změny.";
        }

        return trend;
    }

    private static WearAssessment ComputeWearAssessment(SmartSnapshotRecord snapshot, string? modelName)
    {
        // Build a temporary SmartaData for the vendor mapper
        var smartData = new SmartaData
        {
            DeviceModel = modelName ?? "",
            DeviceType = snapshot.AvailableSpare.HasValue || snapshot.PercentageUsed.HasValue
                ? "NVMe" : "SATA",
            Temperature = snapshot.Temperature,
            PowerOnHours = snapshot.PowerOnHours,
            PowerCycleCount = snapshot.PowerCycleCount,
            ReallocatedSectorCount = snapshot.ReallocatedSectorCount,
            PendingSectorCount = snapshot.PendingSectorCount,
            UncorrectableErrorCount = snapshot.UncorrectableErrorCount,
            WearLevelingCount = snapshot.WearLevelingCount,
            AvailableSpare = snapshot.AvailableSpare,
            PercentageUsed = snapshot.PercentageUsed,
            MediaErrors = snapshot.MediaErrors,
            UnsafeShutdowns = snapshot.UnsafeShutdowns
        };

        return VendorWearMapping.InterpretWearLeveling(smartData);
    }
}
