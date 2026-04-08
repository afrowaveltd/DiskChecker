#pragma warning disable CA1848 // Use LoggerMessage delegates for better performance
#pragma warning disable CA1873 // Avoid boxing of arguments for logging

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Application.Services
{
    /// <summary>
    /// Centralized service for exporting test certificates from TestSession data.
    /// Provides unified API for all ViewModels with automatic downsampling, progress reporting, and error handling.
    /// </summary>
    public class CertificateExportService
    {
        private readonly ICertificateGenerator _certificateGenerator;
        private readonly IDiskCardRepository _diskCardRepository;
        private readonly ILogger<CertificateExportService>? _logger;

        private const int MaxChartPoints = 512;
        private const int SanitizationModulo = 100;
        private const int SanitizationMaxRemainders = 6;

        public CertificateExportService(
            ICertificateGenerator certificateGenerator,
            IDiskCardRepository diskCardRepository,
            ILogger<CertificateExportService>? logger = null)
        {
            _certificateGenerator = certificateGenerator ?? throw new ArgumentNullException(nameof(certificateGenerator));
            _diskCardRepository = diskCardRepository ?? throw new ArgumentNullException(nameof(diskCardRepository));
            _logger = logger;
        }

        /// <summary>
        /// Exportuje certifikát pro zadanou test session s automatickým downsamplingem velkých datasetů.
        /// </summary>
        /// <param name="sessionId">ID test session</param>
        /// <param name="progress">Optional progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Export result containing certificate and PDF path</returns>
        public async Task<CertificateExportResult> ExportCertificateAsync(
            int sessionId,
            IProgress<CertificateExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger?.LogInformation("Starting certificate export for session {SessionId}", sessionId);
                progress?.Report(new CertificateExportProgress("Načítám data testu...", 10));

                // Load session without samples first (performance optimization)
                var session = await _diskCardRepository.GetTestSessionWithoutSamplesAsync(sessionId);
                if (session == null)
                {
                    throw new InvalidOperationException($"Test session s ID {sessionId} nebyla nalezena.");
                }

                // Load disk card
                var card = await _diskCardRepository.GetByIdAsync(session.DiskCardId);
                if (card == null)
                {
                    throw new InvalidOperationException($"Karta disku pro session {sessionId} nebyla nalezena.");
                }

                progress?.Report(new CertificateExportProgress("Načítám vzorky rychlosti...", 30));

                // Load and downsample speed samples based on test type
                if (session.TestType == TestType.Sanitization)
                {
                    await LoadAndDownsampleSanitizationSamplesAsync(session, sessionId, progress, cancellationToken);
                }
                else
                {
                    await LoadAndDownsampleStandardSamplesAsync(session, sessionId, progress, cancellationToken);
                }

                progress?.Report(new CertificateExportProgress("Generuji certifikát...", 70));

                // Generate certificate
                var certificate = await _certificateGenerator.GenerateCertificateAsync(session, card);

                progress?.Report(new CertificateExportProgress("Vytvářím PDF...", 85));

                // Generate PDF
                var pdfPath = await _certificateGenerator.GeneratePdfAsync(certificate);

                progress?.Report(new CertificateExportProgress("Ukládám certifikát...", 95));

                // Save certificate to database
                await _diskCardRepository.CreateCertificateAsync(certificate);

                progress?.Report(new CertificateExportProgress("Hotovo!", 100));

                _logger?.LogInformation(
                    "Certificate export completed successfully. Certificate: {CertificateNumber}, PDF: {PdfPath}",
                    certificate.CertificateNumber,
                    pdfPath);

                return CertificateExportResult.CreateSuccess(certificate, pdfPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Certificate export failed for session {SessionId}", sessionId);
                return CertificateExportResult.CreateFailure(ex.Message, ex);
            }
        }

        /// <summary>
        /// Načte a downsampleuje vzorky pro sanitizaci (velké datasety).
        /// </summary>
        private async Task LoadAndDownsampleSanitizationSamplesAsync(
            TestSession session,
            int sessionId,
            IProgress<CertificateExportProgress>? progress,
            CancellationToken cancellationToken)
        {
            var writeSamples = new List<SpeedSample>();
            var readSamples = new List<SpeedSample>();

            // Load samples progressively in chunks
            for (var remainder = 0; remainder < SanitizationMaxRemainders; remainder++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var progressPercent = 30 + ((70 - 30) * (remainder + 1) / SanitizationMaxRemainders);
                progress?.Report(new CertificateExportProgress(
                    $"Načítám vzorky ({remainder + 1}/{SanitizationMaxRemainders})...",
                    progressPercent));

                try
                {
                    var (writeChunk, readChunk) = await _diskCardRepository.GetSpeedSampleSeriesChunkAsync(
                        sessionId,
                        SanitizationModulo,
                        remainder);

                    writeSamples.AddRange(writeChunk);
                    readSamples.AddRange(readChunk);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to load sample chunk {Remainder} for session {SessionId}", remainder, sessionId);
                    break;
                }
            }

            // Downsample to MaxChartPoints
            session.WriteSamples = DownsampleToLimit(
                writeSamples.OrderBy(s => s.ProgressPercent).ToList(),
                MaxChartPoints);
            session.ReadSamples = DownsampleToLimit(
                readSamples.OrderBy(s => s.ProgressPercent).ToList(),
                MaxChartPoints);

            _logger?.LogInformation(
                "Sanitization samples loaded and downsampled: {WriteCount} write + {ReadCount} read (from {OriginalWrite}/{OriginalRead})",
                session.WriteSamples.Count,
                session.ReadSamples.Count,
                writeSamples.Count,
                readSamples.Count);
        }

        /// <summary>
        /// Načte a downsampleuje vzorky pro standardní testy.
        /// </summary>
        private async Task LoadAndDownsampleStandardSamplesAsync(
            TestSession session,
            int sessionId,
            IProgress<CertificateExportProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (writeSamples, readSamples) = await _diskCardRepository.GetSpeedSampleSeriesAsync(sessionId);

            // Downsample if needed
            session.WriteSamples = DownsampleToLimit(writeSamples, MaxChartPoints);
            session.ReadSamples = DownsampleToLimit(readSamples, MaxChartPoints);

            _logger?.LogInformation(
                "Standard test samples loaded and downsampled: {WriteCount} write + {ReadCount} read (from {OriginalWrite}/{OriginalRead})",
                session.WriteSamples.Count,
                session.ReadSamples.Count,
                writeSamples.Count,
                readSamples.Count);
        }

        /// <summary>
        /// Downsampleuje vzorky na zadaný limit pomocí rovnoměrného výběru.
        /// </summary>
        private static List<SpeedSample> DownsampleToLimit(List<SpeedSample> samples, int maxPoints)
        {
            if (samples == null || samples.Count <= maxPoints)
            {
                return samples ?? new List<SpeedSample>();
            }

            var result = new List<SpeedSample>(maxPoints);
            var step = (double)samples.Count / maxPoints;

            for (int i = 0; i < maxPoints; i++)
            {
                var index = (int)(i * step);
                if (index < samples.Count)
                {
                    result.Add(samples[index]);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Progress report pro export certifikátu.
    /// </summary>
    public record CertificateExportProgress(string Message, int ProgressPercent);

    /// <summary>
    /// Výsledek exportu certifikátu.
    /// </summary>
    public class CertificateExportResult
    {
        public bool IsSuccess { get; init; }
        public DiskCertificate? Certificate { get; init; }
        public string? PdfPath { get; init; }
        public string? ErrorMessage { get; init; }
        public Exception? Exception { get; init; }

        public static CertificateExportResult CreateSuccess(DiskCertificate certificate, string pdfPath) =>
            new() { IsSuccess = true, Certificate = certificate, PdfPath = pdfPath };

        public static CertificateExportResult CreateFailure(string errorMessage, Exception? exception = null) =>
            new() { IsSuccess = false, ErrorMessage = errorMessage, Exception = exception };
    }
}
