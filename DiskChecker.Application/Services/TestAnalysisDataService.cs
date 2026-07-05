using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Application.Services;

/// <summary>
/// Aggregates all persisted analysis data for a test session: session metadata,
/// throughput telemetry, anomaly events, seek samples and temperature samples.
/// </summary>
public class TestAnalysisDataService : ITestAnalysisDataService
{
    private readonly IDiskCardRepository _repository;

    public TestAnalysisDataService(IDiskCardRepository repository)
    {
        _repository = repository;
    }

    public async Task<TestAnalysisData?> GetAnalysisDataAsync(int testSessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var session = await _repository.GetTestSessionWithoutSamplesAsync(testSessionId);
        if (session == null)
        {
            return null;
        }

        var telemetryTask = _repository.GetTelemetrySamplesAsync(testSessionId);
        var anomaliesTask = _repository.GetAnomalyEventsAsync(testSessionId);
        var stallsTask = _repository.GetStallEventsAsync(testSessionId);
        var seekTask = _repository.GetSeekSamplesAsync(testSessionId);
        var temperaturesTask = _repository.GetTemperatureSampleSeriesAsync(testSessionId);

        await Task.WhenAll(telemetryTask, anomaliesTask, stallsTask, seekTask, temperaturesTask);

        return new TestAnalysisData
        {
            Session = session,
            TelemetrySamples = telemetryTask.Result,
            AnomalyEvents = anomaliesTask.Result,
            StallEvents = stallsTask.Result,
            SeekSamples = seekTask.Result,
            TemperatureSamples = temperaturesTask.Result,
            SmartReport = BuildSmartAnalysisReport(session)
        };
    }

    public async Task<List<TestAnalysisSummary>> GetDiskAnalysisSummariesAsync(int diskCardId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var card = await _repository.GetByIdAsync(diskCardId);
        if (card == null)
        {
            return new List<TestAnalysisSummary>();
        }

        var sessions = await _repository.GetTestSessionsAsync(diskCardId);
        var summaries = new List<TestAnalysisSummary>(sessions.Count);

        foreach (var session in sessions.OrderByDescending(s => s.StartedAt))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var telemetryTask = _repository.GetTelemetrySamplesAsync(session.Id);
            var anomaliesTask = _repository.GetAnomalyEventsAsync(session.Id);
            var stallsTask = _repository.GetStallEventsAsync(session.Id);
            var seekTask = _repository.GetSeekSamplesAsync(session.Id);
            await Task.WhenAll(telemetryTask, anomaliesTask, stallsTask, seekTask);

            summaries.Add(new TestAnalysisSummary
            {
                TestSessionId = session.Id,
                DiskCardId = diskCardId,
                DiskModel = card.ModelName,
                SerialNumber = card.SerialNumber,
                TestType = session.TestType,
                StartedAt = session.StartedAt,
                CompletedAt = session.CompletedAt,
                Duration = session.Duration,
                Grade = session.Grade,
                Score = session.Score,
                TelemetrySampleCount = telemetryTask.Result.Count,
                AnomalyCount = anomaliesTask.Result.Count,
                StallCount = stallsTask.Result.Count,
                SeekSampleCount = seekTask.Result.Count
            });
        }

        return summaries;
    }
    private static SmartAnalysisReport BuildSmartAnalysisReport(TestSession session)
    {
        var before = ToSnapshot(session.SmartBefore);
        var after = ToSnapshot(session.SmartAfter) ?? before;
        var report = new SmartAnalysisReport
        {
            Before = before,
            After = after
        };

        if (before == null && after == null)
        {
            return report;
        }

        AddDelta(report.Deltas, "Teplota", before?.Temperature, after?.Temperature, higherIsWorse: true, warningDelta: 8, criticalDelta: 15, "Nárůst teploty během testu.");
        AddDelta(report.Deltas, "Power-on hours", before?.PowerOnHours, after?.PowerOnHours, higherIsWorse: false, warningDelta: long.MaxValue, criticalDelta: long.MaxValue, "Provozní hodiny.");
        AddDelta(report.Deltas, "Power cycles", before?.PowerCycleCount, after?.PowerCycleCount, higherIsWorse: false, warningDelta: long.MaxValue, criticalDelta: long.MaxValue, "Počet startů.");
        AddDelta(report.Deltas, "Reallocated sectors", before?.ReallocatedSectorCount, after?.ReallocatedSectorCount, higherIsWorse: true, warningDelta: 1, criticalDelta: 10, "Nárůst realokovaných sektorů značí degradaci povrchu.");
        AddDelta(report.Deltas, "Pending sectors", before?.PendingSectorCount, after?.PendingSectorCount, higherIsWorse: true, warningDelta: 1, criticalDelta: 5, "Pending sektory jsou nestabilní/obtížně čitelné oblasti.");
        AddDelta(report.Deltas, "Uncorrectable errors", before?.UncorrectableErrorCount, after?.UncorrectableErrorCount, higherIsWorse: true, warningDelta: 1, criticalDelta: 3, "Neopravitelné chyby jsou závažný signál problému média.");
        AddDelta(report.Deltas, "Media errors", before?.MediaErrors, after?.MediaErrors, higherIsWorse: true, warningDelta: 1, criticalDelta: 10, "NVMe media/data integrity errors.");
        AddDelta(report.Deltas, "Unsafe shutdowns", before?.UnsafeShutdowns, after?.UnsafeShutdowns, higherIsWorse: true, warningDelta: 1, criticalDelta: 10, "NVMe unsafe shutdown counter.");

        AddWearIndicator(report.WearIndicators, "NVMe percentage used", after?.PercentageUsed, higherIsWorse: true, warning: 50, critical: 80, "% odhadovaného opotřebení NVMe.");
        AddWearIndicator(report.WearIndicators, "NVMe available spare", after?.AvailableSpare, higherIsWorse: false, warning: 20, critical: 10, "% dostupné rezervy NVMe.");
        AddWearIndicator(report.WearIndicators, "Wear leveling remaining", after?.WearLevelingCount, higherIsWorse: false, warning: 30, critical: 10, "Vendor-specific SSD wear indicator; interpretace nemusí být normalizovaná.");

        if (after?.IsFailing == true)
        {
            report.WearIndicators.Add(new SmartDeltaItem
            {
                Name = "SMART failure prediction",
                After = 1,
                Severity = SmartAnalysisSeverity.Critical,
                Note = string.IsNullOrWhiteSpace(after.FailurePrediction) ? "SMART hlásí predikované selhání." : after.FailurePrediction
            });
        }

        var critical = report.Deltas.Count(d => d.Severity == SmartAnalysisSeverity.Critical) + report.WearIndicators.Count(d => d.Severity == SmartAnalysisSeverity.Critical);
        var warnings = report.Deltas.Count(d => d.Severity == SmartAnalysisSeverity.Warning) + report.WearIndicators.Count(d => d.Severity == SmartAnalysisSeverity.Warning);
        report.Summary = critical > 0
            ? $"SMART: {critical} kritických varování, {warnings} upozornění."
            : warnings > 0
                ? $"SMART: {warnings} upozornění, bez kritických změn."
                : "SMART: bez významných negativních změn.";
        return report;
    }

    private static SmartAnalysisSnapshot? ToSnapshot(SmartaData? smart)
    {
        if (smart == null) return null;
        return new SmartAnalysisSnapshot
        {
            RetrievedAtUtc = smart.RetrievedAtUtc,
            IsHealthy = smart.IsHealthy,
            IsFailing = smart.IsFailing,
            FailurePrediction = smart.FailurePrediction,
            Temperature = smart.Temperature,
            PowerOnHours = smart.PowerOnHours,
            PowerCycleCount = smart.PowerCycleCount,
            ReallocatedSectorCount = smart.ReallocatedSectorCount,
            PendingSectorCount = smart.PendingSectorCount,
            UncorrectableErrorCount = smart.UncorrectableErrorCount,
            WearLevelingCount = smart.WearLevelingCount,
            AvailableSpare = smart.AvailableSpare,
            PercentageUsed = smart.PercentageUsed,
            MediaErrors = smart.MediaErrors,
            UnsafeShutdowns = smart.UnsafeShutdowns
        };
    }

    private static void AddDelta(List<SmartDeltaItem> target, string name, long? before, long? after, bool higherIsWorse, long warningDelta, long criticalDelta, string note)
    {
        if (!before.HasValue && !after.HasValue) return;
        var b = before ?? 0;
        var a = after ?? b;
        var delta = a - b;
        var severity = SmartAnalysisSeverity.Info;
        if (higherIsWorse && delta >= criticalDelta) severity = SmartAnalysisSeverity.Critical;
        else if (higherIsWorse && delta >= warningDelta) severity = SmartAnalysisSeverity.Warning;
        if (delta != 0 || severity != SmartAnalysisSeverity.Info)
        {
            target.Add(new SmartDeltaItem { Name = name, Before = before, After = after, Delta = delta, Severity = severity, Note = note });
        }
    }

    private static void AddWearIndicator(List<SmartDeltaItem> target, string name, int? value, bool higherIsWorse, int warning, int critical, string note)
    {
        if (!value.HasValue) return;
        var severity = SmartAnalysisSeverity.Info;
        if (higherIsWorse)
        {
            if (value.Value >= critical) severity = SmartAnalysisSeverity.Critical;
            else if (value.Value >= warning) severity = SmartAnalysisSeverity.Warning;
        }
        else
        {
            if (value.Value <= critical) severity = SmartAnalysisSeverity.Critical;
            else if (value.Value <= warning) severity = SmartAnalysisSeverity.Warning;
        }
        target.Add(new SmartDeltaItem { Name = name, After = value.Value, Severity = severity, Note = note });
    }

}
