using System.ComponentModel.DataAnnotations;

namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// Database record for test results.
/// </summary>
public class TestRecord
{
    public Guid Id { get; set; }
    
    public Guid DriveId { get; set; }
    
    public DateTime TestDate { get; set; }
    
    [MaxLength(50)]
    public string TestType { get; set; } = string.Empty;
    
    public double AverageSpeed { get; set; }
    public double PeakSpeed { get; set; }
    public double MinSpeed { get; set; }
    public long TotalBytesWritten { get; set; }
    public long TotalBytesRead { get; set; }
    public long TotalBytesTested { get; set; }
    public int ErrorCount { get; set; }
    public int Errors { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsArchived { get; set; }
    public double HealthScore { get; set; }
    
    [MaxLength(10)]
    public string Grade { get; set; } = string.Empty;
    
    public int Score { get; set; }
    
    [MaxLength(500)]
    public string? CertificatePath { get; set; }
    
    [MaxLength(50)]
    public string? SurfaceProfile { get; set; }
    
    [MaxLength(50)]
    public string? SurfaceOperation { get; set; }
    
    [MaxLength(50)]
    public string? SurfaceTechnology { get; set; }
    
    public bool SecureErasePerformed { get; set; }
    public long BytesProcessed { get; set; }
    public string? TestResults { get; set; }

    // Navigation properties
    public DriveRecord Drive { get; set; } = null!;
    public SmartaRecord? SmartaData { get; set; }
    public ICollection<SurfaceTestSampleRecord> SurfaceSamples { get; set; } = new List<SurfaceTestSampleRecord>();
}