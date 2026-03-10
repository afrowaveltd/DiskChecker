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
    
    // ========== Test Sessions ==========
    
    Task<TestSession?> GetTestSessionAsync(int sessionId);
    Task<List<TestSession>> GetTestSessionsAsync(int diskCardId);
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
    /// Get certificate preview as image.
    /// </summary>
    Task<byte[]> GeneratePreviewAsync(DiskCertificate certificate);
    
    /// <summary>
    /// Calculate grade from test results.
    /// </summary>
    (string grade, double score) CalculateGrade(TestSession session);
}

/// <summary>
/// Service for comparing disks.
/// </summary>
public interface IDiskComparisonService
{
    /// <summary>
    /// Compare multiple disks and return rankings.
    /// </summary>
    Task<List<DiskComparisonResult>> CompareAsync(List<int> diskCardIds);
    
    /// <summary>
    /// Get the best disk from a list.
    /// </summary>
    Task<DiskCard?> GetBestDiskAsync(List<int> diskCardIds);
    
    /// <summary>
    /// Get disks suitable for specific use case.
    /// </summary>
    Task<List<DiskCard>> GetRecommendedDisksAsync(string useCase, int count = 5);
    
    /// <summary>
    /// Compare disk performance metrics.
    /// </summary>
    Task<PerformanceComparison> ComparePerformanceAsync(int diskCardId1, int diskCardId2);
}

/// <summary>
/// Result of disk comparison.
/// </summary>
public class DiskComparisonResult
{
    public DiskCard Disk { get; set; } = null!;
    public int Rank { get; set; }
    public double Score { get; set; }
    public string Grade { get; set; } = string.Empty;
    public double AvgWriteSpeed { get; set; }
    public double AvgReadSpeed { get; set; }
    public int ErrorCount { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}

/// <summary>
/// Performance comparison between two disks.
/// </summary>
public class PerformanceComparison
{
    public DiskCard Disk1 { get; set; } = null!;
    public DiskCard Disk2 { get; set; } = null!;
    
    public double WriteSpeedDifference { get; set; }
    public double ReadSpeedDifference { get; set; }
    public int SpeedAdvantagePercent { get; set; }
    
    public string FasterDisk { get; set; } = string.Empty;
    public string MoreReliableDisk { get; set; } = string.Empty;
    public string RecommendedDisk { get; set; } = string.Empty;
    
    public string Summary { get; set; } = string.Empty;
}