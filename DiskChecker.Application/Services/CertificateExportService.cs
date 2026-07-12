#pragma warning disable CA1848 // Use LoggerMessage delegates for better performance
#pragma warning disable CA1873 // Avoid boxing of arguments for logging

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DiskChecker.Application.Services
{
    /// <summary>
    /// Centralized service for exporting test certificates from TestSession data.
    /// Provides unified API for all ViewModels with SQL-level downsampling, progress
    /// reporting, and error handling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Memory strategy.</b>  Sanitization tests on large drives can produce
    /// millions of speed samples.  The previous implementation loaded every
    /// sample into application memory (via <c>ConcurrentBag</c> or
    /// <c>List&lt;SpeedSample&gt;</c>) before downsampling to chart resolution —
    /// on a 128 GB system this exhausted all available RAM and crashed the
    /// application.  This implementation performs downsampling <b>at the SQL
    /// layer</b> using window functions, so at most <see cref="MaxChartPoints"/>
    /// samples per phase are ever materialised in memory.  This keeps memory
    /// usage bounded regardless of the underlying dataset size, while the
    /// certificate quality (grade, aggregate speeds, stall markers) is
    /// preserved because it derives from persisted session aggregates and
    /// the downsampled chart profile.
    /// </para>
    /// </remarks>
    public class CertificateExportService
    {
        private readonly ICertificateGenerator _certificateGenerator;
        private readonly IDiskCardRepository _diskCardRepository;
        private readonly ILocaleProvider? _locale;
        private readonly ILogger<CertificateExportService>? _logger;

        /// <summary>
        /// Maximální počet bodů, které se načítají z databáze pro každý typ
        /// vzorků (zápis, čtení, teplota).  SQL downsampling zajišťuje, že v
        /// paměti není nikdy více než tento počet záznamů, bez ohledu na
        /// celkový počet vzorků v testu.
        /// </summary>
        private const int MaxChartPoints = 512;

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
        /// Exportuje certifikát pro zadanou test session s SQL-level downsamplingem
        /// velkých datasetů.  Paměťová stopa je omezena na <see cref="MaxChartPoints"/>
        /// vzorků na typ, bez ohledu na velikost testu.
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

                // Memory-efficient sample loading: SQL-level downsampling ensures
                // at most MaxChartPoints samples per phase are loaded into memory.
                await LoadDownsampledSamplesAsync(session, sessionId, progress, cancellationToken);

                // Load stall information for certificate critical-signals evaluation
                progress?.Report(new CertificateExportProgress(_locale?.GetString("CertificateExport.LoadingStallInfo", "Načítám informace o zaseknutí...") ?? "Načítám informace o zaseknutí...", 60));

                var (totalSamples, stalledSamples) = await _diskCardRepository.GetSpeedSampleStallInfoAsync(sessionId, cancellationToken);
                session.StalledSampleCount = stalledSamples;

                progress?.Report(new CertificateExportProgress(_locale?.GetString("CertificateExport.LoadingTemperatures", "Načítám teploty...") ?? "Načítám teploty...", 65));

                session.TemperatureSamples = await _diskCardRepository.GetTemperatureSampleSeriesDownsampledAsync(sessionId, MaxChartPoints, cancellationToken);

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
        /// Načte downsampleované vzorky rychlosti (zápis + čtení) pomocí SQL
        /// downsamplingu.  Do paměti se dostane maximálně
        /// <see cref="MaxChartPoints"/> vzorků pro zápis a stejně pro čtení,
        /// bez ohledu na typ testu nebo velikost datasetu.  Tím je paměťová
        /// stopa konstantní a predikovatelná.
        /// </summary>
        private async Task LoadDownsampledSamplesAsync(
            TestSession session,
            int sessionId,
            IProgress<CertificateExportProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new CertificateExportProgress(
                _locale?.GetString("CertificateExport.DownsamplingSamples", "Vybírám reprezentativní vzorky z databáze...") ?? "Vybírám reprezentativní vzorky z databáze...",
                35));

            // SQL-level downsampling: the database returns only ~MaxChartPoints
            // rows per phase using ROW_NUMBER() window function. This is the
            // key memory optimization — no matter how many millions of samples
            // the test produced, we only materialize a small representative
            // subset in application memory.
            var (writeSamples, readSamples) = await _diskCardRepository.GetSpeedSampleSeriesDownsampledAsync(
                sessionId, MaxChartPoints, cancellationToken);

            // The SQL query already returns samples ordered by row number
            // (which corresponds to chronological order), so no additional
            // sorting is needed here.
            session.WriteSamples = writeSamples;
            session.ReadSamples = readSamples;

            progress?.Report(new CertificateExportProgress(
                string.Format(_locale?.GetString("CertificateExport.SamplesLoaded", "Načteno {0} vzorků zápisu + {1} vzorků čtení") ?? "Načteno {0} vzorků zápisu + {1} vzorků čtení", writeSamples.Count, readSamples.Count),
                55));

            _logger?.LogInformation(
                "Samples loaded via SQL downsampling: {WriteCount} write + {ReadCount} read (max {MaxPoints} each)",
                writeSamples.Count,
                readSamples.Count,
                MaxChartPoints);
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