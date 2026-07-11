#pragma warning disable CA1848 // Use LoggerMessage delegates for better performance
#pragma warning disable CA1873 // Avoid boxing of arguments for logging

using System;
using System.Collections.Concurrent;
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
    /// Optimized for large datasets with parallel loading and processing.
    /// </summary>
    public class CertificateExportService
    {
        private readonly ICertificateGenerator _certificateGenerator;
        private readonly IDiskCardRepository _diskCardRepository;
        private readonly ILocaleProvider? _locale;
        private readonly ILogger<CertificateExportService>? _logger;

        private const int MaxChartPoints = 512;
        private const int SanitizationModulo = 100;
        private const int SanitizationMaxRemainders = 6;

        // Detect number of CPU cores for parallel processing
        private static readonly int ProcessorCount = Environment.ProcessorCount;

        public CertificateExportService(
            ICertificateGenerator certificateGenerator,
            IDiskCardRepository diskCardRepository,
            ILocaleProvider? locale = null,
            ILogger<CertificateExportService>? logger = null)
        {
            _certificateGenerator = certificateGenerator ?? throw new ArgumentNullException(nameof(certificateGenerator));
            _diskCardRepository = diskCardRepository ?? throw new ArgumentNullException(nameof(diskCardRepository));
            _locale = locale;
            _logger = logger;
        }

        /// <summary>
        /// Exportuje certifikát pro zadanou test session s automatickým downsamplingem velkých datasetů.
        /// Využívá paralelní zpracování pro urychlení načítání a zpracování vzorků.
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
                progress?.Report(new CertificateExportProgress(_locale?.GetString("CertificateExport.LoadingTestData", "Načítám data testu...") ?? "Načítám data testu...", 10));

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

                progress?.Report(new CertificateExportProgress(_locale?.GetString("CertificateExport.LoadingSpeedSamples", "Načítám vzorky rychlosti...") ?? "Načítám vzorky rychlosti...", 30));

                // Load and downsample speed samples based on test type
                if (session.TestType == TestType.Sanitization)
                {
                    await LoadAndDownsampleSanitizationSamplesAsync(session, sessionId, progress, cancellationToken);
                }
                else
                {
                    await LoadAndDownsampleStandardSamplesAsync(session, sessionId, progress, cancellationToken);
                }

                // Load temperature samples
                progress?.Report(new CertificateExportProgress(_locale?.GetString("CertificateExport.LoadingTemperatures", "Načítám teploty...") ?? "Načítám teploty...", 65));

                session.TemperatureSamples = DownsampleToLimit(
                    await _diskCardRepository.GetTemperatureSampleSeriesAsync(sessionId),
                    MaxChartPoints);

                progress?.Report(new CertificateExportProgress(_locale?.GetString("CertificateExport.GeneratingCertificate", "Generuji certifikát...") ?? "Generuji certifikát...", 70));

                // Generate certificate
                var certificate = await _certificateGenerator.GenerateCertificateAsync(session, card);

                progress?.Report(new CertificateExportProgress(_locale?.GetString("CertificateExport.GeneratingPdf", "Vytvářím PDF...") ?? "Vytvářím PDF...", 85));

                // Generate PDF
                var pdfPath = await _certificateGenerator.GeneratePdfAsync(certificate);

                progress?.Report(new CertificateExportProgress(_locale?.GetString("CertificateExport.SavingCertificate", "Ukládám certifikát...") ?? "Ukládám certifikát...", 95));

                // Save certificate to database
                await _diskCardRepository.CreateCertificateAsync(certificate);

                progress?.Report(new CertificateExportProgress(_locale?.GetString("CertificateExport.Done", "Hotovo!") ?? "Hotovo!", 100));

                _logger?.LogInformation(
                    "Certificate export completed successfully. Certificate: {CertificateNumber}, PDF: {PdfPath}",
                    certificate.CertificateNumber,
                    pdfPath);

                return CertificateExportResult.CreateSuccess(certificate, pdfPath);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Certificate export cancelled for session {SessionId}", sessionId);
                return CertificateExportResult.CreateFailure("Export byl zrušen.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Certificate export failed for session {SessionId}", sessionId);
                return CertificateExportResult.CreateFailure(ex.Message, ex);
            }
        }

        /// <summary>
        /// Načte a downsampleuje vzorky pro sanitizaci (velké datasety) s paralelním zpracováním.
        /// Využívá více vláken pro současné načítání chunků z databáze.
        /// </summary>
        private async Task LoadAndDownsampleSanitizationSamplesAsync(
            TestSession session,
            int sessionId,
            IProgress<CertificateExportProgress>? progress,
            CancellationToken cancellationToken)
        {
            // Use ConcurrentBag for thread-safe collection from parallel tasks
            var writeBag = new ConcurrentBag<SpeedSample>();
            var readBag = new ConcurrentBag<SpeedSample>();

            // Load samples in parallel chunks
            var loadTasks = new List<Task>();
            var completedRemainders = 0;
            var lockObj = new object();

            for (var remainder = 0; remainder < SanitizationMaxRemainders; remainder++)
            {
                var r = remainder; // Capture for closure
                loadTasks.Add(Task.Run(async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var (writeChunk, readChunk) = await _diskCardRepository.GetSpeedSampleSeriesChunkAsync(
                            sessionId,
                            SanitizationModulo,
                            r);

                        foreach (var sample in writeChunk)
                            writeBag.Add(sample);
                        foreach (var sample in readChunk)
                            readBag.Add(sample);

                        lock (lockObj)
                        {
                            completedRemainders++;
                            var progressPercent = 30 + ((65 - 30) * completedRemainders / SanitizationMaxRemainders);
                            progress?.Report(new CertificateExportProgress(
                                string.Format(_locale?.GetString("CertificateExport.LoadingSamplesProgress", "Načítám vzorky ({0}/{1})...") ?? "Načítám vzorky ({0}/{1})...", completedRemainders, SanitizationMaxRemainders),
                                progressPercent));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to load sample chunk {Remainder} for session {SessionId}", r, sessionId);
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(loadTasks);

            // Sort and downsample in parallel
            var sortTask = Task.Run(() =>
            {
                var sortedWrite = writeBag.OrderBy(s => s.ProgressPercent).ToList();
                session.WriteSamples = DownsampleToLimit(sortedWrite, MaxChartPoints);
                return session.WriteSamples.Count;
            }, cancellationToken);

            var sortReadTask = Task.Run(() =>
            {
                var sortedRead = readBag.OrderBy(s => s.ProgressPercent).ToList();
                session.ReadSamples = DownsampleToLimit(sortedRead, MaxChartPoints);
                return session.ReadSamples.Count;
            }, cancellationToken);

            await Task.WhenAll(sortTask, sortReadTask);

            _logger?.LogInformation(
                "Sanitization samples loaded and downsampled: {WriteCount} write + {ReadCount} read (from {OriginalWrite}/{OriginalRead})",
                sortTask.Result,
                sortReadTask.Result,
                writeBag.Count,
                readBag.Count);
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

            // Downsample write and read in parallel
            var downsampleWriteTask = Task.Run(() => DownsampleToLimit(writeSamples, MaxChartPoints), cancellationToken);
            var downsampleReadTask = Task.Run(() => DownsampleToLimit(readSamples, MaxChartPoints), cancellationToken);

            await Task.WhenAll(downsampleWriteTask, downsampleReadTask);

            session.WriteSamples = downsampleWriteTask.Result;
            session.ReadSamples = downsampleReadTask.Result;

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
        private static List<TemperatureSample> DownsampleToLimit(List<TemperatureSample> samples, int maxPoints)
        {
            if (samples == null || samples.Count <= maxPoints)
            {
                return samples ?? new List<TemperatureSample>();
            }

            var result = new List<TemperatureSample>(maxPoints);
            var step = (double)samples.Count / maxPoints;
            for (int i = 0; i < maxPoints; i++)
            {
                var index = (int)(i * step);
                if (index < samples.Count)
                    result.Add(samples[index]);
            }
            return result;
        }

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
