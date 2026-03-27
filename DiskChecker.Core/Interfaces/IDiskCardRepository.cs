using System;
using System.Collections.Generic;
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