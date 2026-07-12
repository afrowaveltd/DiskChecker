using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Interfaces;

/// <summary>
/// Repository for managing disk cards.
/// </summary>
public interface IDiskCardRepository
{
    // ========== CRUD Operations ==========
    
    Task<DiskCard?> GetByIdAsync(int id);
    Task<DiskCard?> GetBySerialNumberAsync(string serialNumber);
    Task<DiskCard?> GetByDevicePathAsync(string devicePath);
    Task<List<DiskCard>> GetAllAsync();
    Task<List<DiskCard>> GetActiveAsync();
    Task<List<DiskCard>> GetArchivedAsync();
    
    Task<DiskCard> CreateAsync(DiskCard card);
    Task<DiskCard> UpdateAsync(DiskCard card);
    Task DeleteAsync(int id);
    Task ArchiveAsync(int id, ArchiveReason reason, string? notes = null);
    Task RestoreAsync(int id);

    /// <summary>
    /// Sloučí duplicitní karty disku a převede navázaná data na primární kartu.
    /// </summary>
    Task<int> MergeDuplicateCardsAsync();

    // ========== SMART Snapshots ==========

    /// <summary>
    /// Uloží historický SMART snapshot pro trendovou analýzu.
    /// </summary>
    Task<SmartSnapshotRecord> CreateSmartSnapshotAsync(SmartSnapshotRecord snapshot);

    /// <summary>
    /// Načte všechny SMART snapshoty pro daný disk, seřazené chronologicky.
    /// </summary>
    Task<List<SmartSnapshotRecord>> GetSmartSnapshotsAsync(int diskCardId);

    /// <summary>
    /// Načte SMART snapshoty pro daný disk v časovém rozsahu.
    /// </summary>
    Task<List<SmartSnapshotRecord>> GetSmartSnapshotsInRangeAsync(int diskCardId, DateTime fromUtc, DateTime toUtc);

    /// <summary>
    /// Smaže všechny SMART snapshoty pro daný disk.
    /// </summary>
    Task DeleteSmartSnapshotsAsync(int diskCardId);
    
    // ========== Test Sessions ==========
    
    Task<TestSession?> GetTestSessionAsync(int sessionId);
    
    /// <summary>
    /// Načte test session bez velkých kolekcí (WriteSamples, ReadSamples).
    /// Vhodné pro optimalizované načítání, kde se kolekce načítají dodatečně.
    /// </summary>
    Task<TestSession?> GetTestSessionWithoutSamplesAsync(int sessionId);
    
    /// <summary>
    /// Načte omezený počet chyb z test session pro zobrazení v reportu.
    /// </summary>
    Task<List<TestError>> GetTestErrorsAsync(int sessionId, int maxErrors = 100);
    
    Task<List<TestSession>> GetTestSessionsAsync(int diskCardId);

    /// <summary>
    /// Načte uložené rychlostní vzorky pro zadanou test session bez načtení celé session.
    /// </summary>
    Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> GetSpeedSampleSeriesAsync(int sessionId);
    
    /// <summary>
    /// Načte podmnožinu rychlostních vzorků pro zadanou test session pomocí modulárního výběru.
    /// Umožňuje progresivní vykreslení grafu po dávkách bez načtení celé série najednou.
    /// </summary>
    Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> GetSpeedSampleSeriesChunkAsync(int sessionId, int modulo, int remainder);

    /// <summary>
    /// Načte uložené teplotní vzorky pro zadanou test session bez načtení celé session.
    /// </summary>
    Task<List<TemperatureSample>> GetTemperatureSampleSeriesAsync(int sessionId);

    /// <summary>
    /// Načte rovnoměrně rozloženou podmnožinu rychlostních vzorků přímo z databáze
    /// tak, aby v paměti nebylo nikdy více než <paramref name="maxPoints"/> záznamů
    /// pro zápis i čtení.  Tím se předchází OOM při generování certifikátů z
    /// rozsáhlých sanitizačních testů, které mohou obsahovat miliony vzorků.
    /// </summary>
    Task<(List<SpeedSample> WriteSamples, List<SpeedSample> ReadSamples)> GetSpeedSampleSeriesDownsampledAsync(
        int sessionId, int maxPoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// Načte rovnoměrně rozloženou podmnožinu teplotních vzorků přímo z databáze
    /// s limitem <paramref name="maxPoints"/> záznamů v paměti.
    /// </summary>
    Task<List<TemperatureSample>> GetTemperatureSampleSeriesDownsampledAsync(
        int sessionId, int maxPoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vrátí celkový počet záznamů vzorků rychlosti (zápis + čtení) a počet
    /// vzorků označených jako I/O stall.  Slouží k vyhodnocení kritických
    /// signálů bez načítání celých kolekcí do paměti.
    /// </summary>
    Task<(int TotalSamples, int StalledSamples)> GetSpeedSampleStallInfoAsync(
        int sessionId, CancellationToken cancellationToken = default);

    /// <summary>Persistuje throughput telemetrii pro pozdější detailní analýzu a zoom grafů.</summary>
    Task CreateTelemetrySamplesAsync(int sessionId, TelemetrySamplePhase phase, IReadOnlyCollection<SpeedSample> samples, bool replacePhase = true);

    /// <summary>Načte throughput telemetrii pro test session.</summary>
    Task<List<TestTelemetrySample>> GetTelemetrySamplesAsync(int sessionId, TelemetrySamplePhase? phase = null);

    /// <summary>Persistuje detekované výkonové anomálie a jejich high-res vzorky do analytických tabulek.</summary>
    Task CreateAnomalyEventsAsync(int sessionId, IReadOnlyCollection<SpeedAnomaly> anomalies, bool replaceExisting = true);

    /// <summary>Načte detekované výkonové anomálie pro test session.</summary>
    Task<List<TestAnomalyEvent>> GetAnomalyEventsAsync(int sessionId, TelemetrySamplePhase? phase = null);

    /// <summary>Persistuje intervaly zamrznutí zařízení odvozené z telemetrie.</summary>
    Task CreateStallEventsAsync(int sessionId, TelemetrySamplePhase phase, IReadOnlyCollection<TestStallEvent> events, bool replacePhase = true);

    /// <summary>Načte intervaly zamrznutí zařízení pro test session.</summary>
    Task<List<TestStallEvent>> GetStallEventsAsync(int sessionId, TelemetrySamplePhase? phase = null);

    /// <summary>Persistuje kompletní seek vzorky pro detailní pozdější analýzu.</summary>
    Task CreateSeekSamplesAsync(int sessionId, SeekTestType testType, IReadOnlyCollection<SeekLatencySample> samples);

    /// <summary>Načte kompletní seek vzorky pro test session.</summary>
    Task<List<SeekSampleRecord>> GetSeekSamplesAsync(int sessionId, SeekTestType? testType = null);
    
    Task<TestSession> CreateTestSessionAsync(TestSession session);
    Task<TestSession> UpdateTestSessionAsync(TestSession session);
    Task DeleteTestSessionAsync(int sessionId);
    
    // ========== Certificates ==========
    
    Task<DiskCertificate?> GetCertificateAsync(int certificateId);
    Task<DiskCertificate?> GetLatestCertificateAsync(int diskCardId);
    Task<List<DiskCertificate>> GetCertificatesAsync(int diskCardId);
    Task<DiskCertificate> CreateCertificateAsync(DiskCertificate certificate);
    Task UpdateCertificateAsync(DiskCertificate certificate);
    
    // ========== Comparisons ==========
    
    Task<List<DiskCard>> GetBestDisksAsync(int count = 10);
    Task<List<DiskCard>> GetByGradeAsync(string grade);
    Task<Dictionary<string, List<DiskCard>>> GetByHealthStatusAsync();
}

/// <summary>
/// Service for collecting metrics during tests.
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Start collecting metrics for a test session.
    /// </summary>
    void StartSession(TestSession session);
    
    /// <summary>
    /// Record a speed sample during write.
    /// </summary>
    void RecordWriteSpeed(double speedMBps, double progressPercent, long bytesProcessed);
    
    /// <summary>
    /// Record a speed sample during read.
    /// </summary>
    void RecordReadSpeed(double speedMBps, double progressPercent, long bytesProcessed);
    
    /// <summary>
    /// Record a temperature sample.
    /// </summary>
    void RecordTemperature(int temperatureCelsius, string phase, double progressPercent);
    
    /// <summary>
    /// Record an error.
    /// </summary>
    void RecordError(string errorCode, string message, string phase, bool isCritical, string? details = null);
    
    /// <summary>
    /// Complete the session and calculate final metrics.
    /// </summary>
    Task<TestSession> CompleteSessionAsync();
    
    /// <summary>
    /// Get current session progress.
    /// </summary>
    (double WriteProgress, double ReadProgress, double CurrentSpeed) GetProgress();
}

/// <summary>
/// Service for generating disk certificates.
/// </summary>
public interface ICertificateGenerator
{
    /// <summary>
    /// Generate a certificate from test session.
    /// </summary>
    Task<DiskCertificate> GenerateCertificateAsync(TestSession session, DiskCard diskCard);
    
    /// <summary>
    /// Generates and stores a cached chart image for the specified test session.
    /// </summary>
    Task<string?> GenerateAndStoreChartImageAsync(TestSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a cached chart image exists for the specified test session and returns its path.
    /// </summary>
    Task<string?> EnsureChartImageAsync(TestSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate PDF from certificate.
    /// </summary>
    Task<string> GeneratePdfAsync(DiskCertificate certificate);

    /// <summary>
    /// Generate a printable disk label image from certificate.
    /// </summary>
    Task<string> GenerateLabelAsync(DiskCertificate certificate);
    
    /// <summary>
    /// Get certificate preview as image.
    /// </summary>
    Task<byte[]> GeneratePreviewAsync(DiskCertificate certificate);
    
    /// <summary>
    /// Calculate grade from test results.
    /// </summary>
    (string grade, double score) CalculateGrade(TestSession session);
}