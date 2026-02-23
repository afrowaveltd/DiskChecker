using DiskChecker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Infrastructure.Persistence;

public class DiskCheckerDbContext : DbContext
{
    public DiskCheckerDbContext(DbContextOptions<DiskCheckerDbContext> options) 
        : base(options)
    {
    }

    public DbSet<TestRecord> Tests { get; set; } = null!;
    public DbSet<SmartaRecord> SmartaData { get; set; } = null!;
    public DbSet<DriveRecord> Drives { get; set; } = null!;
    public DbSet<SurfaceTestSampleRecord> SurfaceTestSamples { get; set; } = null!;
    public DbSet<EmailSettingsRecord> EmailSettings { get; set; } = null!;
    public DbSet<ReplicationQueueRecord> ReplicationQueue { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DriveRecord>()
            .HasIndex(d => d.SerialNumber)
            .IsUnique();

        modelBuilder.Entity<TestRecord>()
            .HasIndex(t => t.DriveId)
            .IsUnique(false);

        modelBuilder.Entity<TestRecord>()
            .HasIndex(t => t.TestDate);

        modelBuilder.Entity<SurfaceTestSampleRecord>()
            .HasIndex(s => s.TestId);

        modelBuilder.Entity<TestRecord>()
            .HasMany(t => t.SurfaceSamples)
            .WithOne(s => s.Test)
            .HasForeignKey(s => s.TestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EmailSettingsRecord>()
            .HasIndex(s => s.Id)
            .IsUnique();
    }
}

public class DriveRecord
{
    public Guid Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ModelFamily { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public int TotalTests { get; set; }
    
    public ICollection<TestRecord> Tests { get; set; } = new List<TestRecord>();
}

public class SmartaRecord
{
    public Guid Id { get; set; }
    public Guid TestId { get; set; }
    public int PowerOnHours { get; set; }
    public long ReallocatedSectorCount { get; set; }
    public long PendingSectorCount { get; set; }
    public long UncorrectableErrorCount { get; set; }
    public double Temperature { get; set; }
    public int? WearLevelingCount { get; set; }
    
    public TestRecord Test { get; set; } = null!;
}

public class TestRecord
{
    public Guid Id { get; set; }
    public Guid DriveId { get; set; }
    public DateTime TestDate { get; set; }
    public string TestType { get; set; } = string.Empty;
    public double AverageSpeed { get; set; }
    public double PeakSpeed { get; set; }
    public double MinSpeed { get; set; }
    public long TotalBytesWritten { get; set; }
    /// <summary>
    /// Gets or sets the total bytes processed during the test.
    /// </summary>
    public long TotalBytesTested { get; set; }
    public int Errors { get; set; }
    public QualityGrade Grade { get; set; }
    public double Score { get; set; }
    public string CertificatePath { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the surface test profile used for this test.
    /// </summary>
    public SurfaceTestProfile? SurfaceProfile { get; set; }
    /// <summary>
    /// Gets or sets the surface test operation used for this test.
    /// </summary>
    public SurfaceTestOperation? SurfaceOperation { get; set; }
    /// <summary>
    /// Gets or sets the drive technology used for this test.
    /// </summary>
    public DriveTechnology? SurfaceTechnology { get; set; }
    /// <summary>
    /// Gets or sets whether secure erase was performed.
    /// </summary>
    public bool? SecureErasePerformed { get; set; }

    public DriveRecord Drive { get; set; } = null!;
    public SmartaRecord SmartaData { get; set; } = null!;
    /// <summary>
    /// Gets or sets the surface test samples captured for the test.
    /// </summary>
    public ICollection<SurfaceTestSampleRecord> SurfaceSamples { get; set; } = new List<SurfaceTestSampleRecord>();
}

/// <summary>
/// Represents a speed sample recorded during a surface test.
/// </summary>
public class SurfaceTestSampleRecord
{
    /// <summary>
    /// Gets or sets the sample identifier.
    /// </summary>
    public Guid Id { get; set; }
    /// <summary>
    /// Gets or sets the related test identifier.
    /// </summary>
    public Guid TestId { get; set; }
    /// <summary>
    /// Gets or sets the byte offset where the sample was recorded.
    /// </summary>
    public long OffsetBytes { get; set; }
    /// <summary>
    /// Gets or sets the block size in bytes.
    /// </summary>
    public int BlockSizeBytes { get; set; }
    /// <summary>
    /// Gets or sets the throughput in MB/s.
    /// </summary>
    public double ThroughputMbps { get; set; }
    /// <summary>
    /// Gets or sets the timestamp in UTC.
    /// </summary>
    public DateTime TimestampUtc { get; set; }
    /// <summary>
    /// Gets or sets the error count for the sample window.
    /// </summary>
    public int ErrorCount { get; set; }
    /// <summary>
    /// Gets or sets the navigation property for the test.
    /// </summary>
    public TestRecord Test { get; set; } = null!;
}

/// <summary>
/// Stores SMTP settings in the database.
/// </summary>
public class EmailSettingsRecord
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }
    /// <summary>
    /// Gets or sets the SMTP host.
    /// </summary>
    public string Host { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the SMTP port.
    /// </summary>
    public int Port { get; set; }
    /// <summary>
    /// Gets or sets whether SSL is used.
    /// </summary>
    public bool UseSsl { get; set; }
    /// <summary>
    /// Gets or sets the SMTP username.
    /// </summary>
    public string UserName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the SMTP password.
    /// </summary>
    public string Password { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the sender name.
    /// </summary>
    public string FromName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the sender address.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the last update time.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}

public class ReplicationQueueRecord
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public bool IsSent { get; set; }
    public int RetryCount { get; set; }
    public string? Payload { get; set; }
}
