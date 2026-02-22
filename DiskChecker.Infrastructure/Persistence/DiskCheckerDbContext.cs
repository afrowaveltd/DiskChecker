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
    public int Errors { get; set; }
    public QualityGrade Grade { get; set; }
    public double Score { get; set; }
    public string CertificatePath { get; set; } = string.Empty;
    
    public DriveRecord Drive { get; set; } = null!;
    public SmartaRecord SmartaData { get; set; } = null!;
}
