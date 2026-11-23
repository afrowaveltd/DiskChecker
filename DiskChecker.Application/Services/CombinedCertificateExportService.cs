using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.Core.Services;

namespace DiskChecker.Application.Services;

/// <summary>
/// Exports two or three historical test sessions as one PDF. A session may
/// predate stored certificates: in that case a transient certificate is
/// reconstructed WITHOUT creating or updating database rows.
/// </summary>
public sealed class CombinedCertificateExportService
{
    private readonly IDiskCardRepository _repository;
    private readonly ICertificateGenerator _generator;

    public CombinedCertificateExportService(IDiskCardRepository repository, ICertificateGenerator generator)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    /// <summary>
    /// Prefer explicit certificate checkboxes. Otherwise use the selected
    /// history rows (which may contain test sessions with no PDF yet).
    /// </summary>
    public static IReadOnlyList<int> SelectSessions(
        IReadOnlyList<int> historySelection,
        IReadOnlyList<DiskCertificate> checkedCertificates)
    {
        ArgumentNullException.ThrowIfNull(historySelection);
        ArgumentNullException.ThrowIfNull(checkedCertificates);
        return checkedCertificates.Count > 0
            ? checkedCertificates.Select(c => c.TestSessionId).ToArray()
            : historySelection.ToArray();
    }

    /// <summary>
    /// Exports explicitly selected sessions, optionally bound to exact saved
    /// certificates selected in the Certificates tab. A history selection
    /// instead prefers the latest existing certificate per test session and
    /// reconstructs any missing legacy certificate in memory.
    /// </summary>
    public async Task<string> ExportAsync(
        DiskCard card,
        IReadOnlyList<int> sessionIds,
        IReadOnlyList<int>? certificateIds = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(sessionIds);

        if (card.Id <= 0 || sessionIds.Count is < 2 or > 3 ||
            sessionIds.Any(id => id <= 0) ||
            sessionIds.Distinct().Count() != sessionIds.Count)
        {
            throw new ArgumentException(
                "Vyberte dva nebo tři různé testy stejné uložené karty disku.",
                nameof(sessionIds));
        }

        if (certificateIds is { Count: > 0 } && certificateIds.Count != sessionIds.Count)
            throw new ArgumentException("Výběr certifikátů a testů si neodpovídá.", nameof(certificateIds));

        var saved = certificateIds is { Count: > 0 }
            ? null
            : await _repository.GetCertificatesAsync(card.Id);

        var certificates = new List<DiskCertificate>(sessionIds.Count);
        foreach (int index in Enumerable.Range(0, sessionIds.Count))
        {
            int sessionId = sessionIds[index];
            var session = await _repository.GetTestSessionWithoutSamplesAsync(sessionId)
                ?? throw new InvalidOperationException($"Test {sessionId} již v databázi neexistuje.");
            if (session.DiskCardId != card.Id)
                throw new InvalidOperationException("Vybraný test patří k jiné kartě disku.");

            DiskCertificate? certificate;
            if (certificateIds is { Count: > 0 })
            {
                certificate = await _repository.GetCertificateAsync(certificateIds[index])
                    ?? throw new InvalidOperationException("Vybraný certifikát již neexistuje.");
                if (certificate.DiskCardId != card.Id || certificate.TestSessionId != sessionId)
                    throw new InvalidOperationException("Certifikát neodpovídá vybranému disku nebo testu.");
            }
            else
            {
                var latest = saved!
                    .Where(c => c.TestSessionId == sessionId)
                    .OrderByDescending(c => c.GeneratedAt)
                    .FirstOrDefault();

                certificate = latest == null ? null : await _repository.GetCertificateAsync(latest.Id);
                if (certificate != null &&
                    (certificate.DiskCardId != card.Id || certificate.TestSessionId != sessionId))
                {
                    throw new InvalidOperationException("Historický certifikát neodpovídá kartě disku.");
                }
            }

            if (certificate == null)
            {
                // Older releases may have test history but no persisted
                // certificate. Reconstruct it using the same bounded data and
                // EXACT stall count as the normal single-certificate exporter.
                // A wrong missing-stall count could falsely recommend a
                // failing disk; never infer health from an empty sample list.
                var (_, stalledSamples) =
                    await _repository.GetSpeedSampleStallInfoAsync(session.Id);
                session.StalledSampleCount = stalledSamples;

                var (writeSamples, readSamples) =
                    await _repository.GetSpeedSampleSeriesDownsampledAsync(session.Id, 512);
                session.WriteSamples = writeSamples;
                session.ReadSamples = readSamples;
                session.TemperatureSamples =
                    await _repository.GetTemperatureSampleSeriesDownsampledAsync(session.Id, 512);

                certificate = await _generator.GenerateCertificateAsync(session, card);
                certificate.CertificateNumber = $"HIST-{card.Id}-{session.Id}";
            }

            // The issue date on the certificate remains GeneratedAt. Its
            // source session supplies the distinct, original TEST date for
            // the cover page, without modifying the persisted test record.
            certificate.TestSession = session;

            if (session.TestType == TestType.Seek)
            {
                // SEEK statistics can be non-persisted runtime properties.
                var reconstructed = await _generator.GenerateCertificateAsync(session, card);
                certificate.SeekAvgLatencyMs ??= reconstructed.SeekAvgLatencyMs;
                certificate.SeekMinLatencyMs ??= reconstructed.SeekMinLatencyMs;
                certificate.SeekMaxLatencyMs ??= reconstructed.SeekMaxLatencyMs;
                certificate.SeekStdDevLatencyMs ??= reconstructed.SeekStdDevLatencyMs;
                certificate.SeekMedianLatencyMs ??= reconstructed.SeekMedianLatencyMs;
                certificate.SeekP95LatencyMs ??= reconstructed.SeekP95LatencyMs;
                certificate.SeekP99LatencyMs ??= reconstructed.SeekP99LatencyMs;
                certificate.SeekTotalCount ??= reconstructed.SeekTotalCount;
                certificate.SeekErrorCount ??= reconstructed.SeekErrorCount;
                certificate.SeekTestSummary ??= reconstructed.SeekTestSummary;
                if (certificate.SeekTypeSummaries.Count == 0)
                    certificate.SeekTypeSummaries = reconstructed.SeekTypeSummaries;

                var samples = await _repository.GetSeekSamplesDownsampledAsync(session.Id, 512);
                certificate.SeekLatencyPoints = samples
                    .Where(p => !p.HasError && p.LatencyMs > 0)
                    .OrderBy(p => p.Index).Select(p => p.LatencyMs).ToList();
                if (certificate.SeekLatencyPoints.Count == 0)
                    certificate.SeekLatencyPoints = reconstructed.SeekLatencyPoints;
            }
            else
            {
                var (write, read) = await _repository.GetSpeedSampleSeriesDownsampledAsync(session.Id, 512);
                certificate.WriteProfilePoints = write.Select(p => p.SpeedMBps).ToList();
                certificate.ReadProfilePoints = read.Select(p => p.SpeedMBps).ToList();
            }

            certificates.Add(certificate);
        }

        // Validate all test categories and disk identities BEFORE PDF creation.
        CombinedCertificatePolicy.Validate(certificates);
        return await _generator.GenerateCombinedPdfAsync(certificates);
    }
}
