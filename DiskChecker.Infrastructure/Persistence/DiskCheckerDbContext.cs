using DiskChecker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.Infrastructure.Persistence
{
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
      public DbSet<DiskCard> DiskCards { get; set; } = null!;
      public DbSet<TestReport> TestReports { get; set; } = null!;

      protected override void OnModelCreating(ModelBuilder modelBuilder)
      {
         base.OnModelCreating(modelBuilder);
         // Ignore transient DTOs used by services/UI so EF Core does not try to map them as entities.
         modelBuilder.Ignore<CoreDriveInfo>();
         modelBuilder.Ignore<SmartaData>();
         modelBuilder.Ignore<QualityRating>();
         modelBuilder.Ignore<SmartCheckResult>();
        modelBuilder.Ignore<SurfaceTestRequest>();
        modelBuilder.Ignore<SurfaceTestSample>();
        modelBuilder.Ignore<SurfaceTestProgress>();
        modelBuilder.Ignore<SurfaceTestResult>();
         modelBuilder.Ignore<SmartaAttributeItem>();
         modelBuilder.Ignore<SmartaSelfTestStatus>();
         modelBuilder.Ignore<SmartaSelfTestEntry>();
         modelBuilder.Ignore<SmartaSelfTestReport>();
         modelBuilder.Ignore<TemperatureHistoryPoint>();
         modelBuilder.Ignore<SpeedSample>();
         modelBuilder.Ignore<TestHistoryItem>();
         modelBuilder.Ignore<CompareItem>();
         modelBuilder.Ignore<DriveCompareItem>();


         // DiskCard indexes
         modelBuilder.Entity<DiskCard>()
             .HasIndex(d => d.SerialNumber)
             .IsUnique();

         modelBuilder.Entity<DiskCard>()
             .HasIndex(d => d.LastTestedDate)
             .IsUnique(false);

         modelBuilder.Entity<DiskCard>()
             .HasIndex(d => d.Status);

         // TestReport indexes
        // Ensure TestReport has an explicitly configured primary key (property is named ReportId)
        modelBuilder.Entity<TestReport>().HasKey(r => r.ReportId);
         modelBuilder.Entity<TestReport>()
             .HasIndex(r => r.DiskCardId);

         modelBuilder.Entity<TestReport>()
             .HasIndex(r => r.TestDate);

         modelBuilder.Entity<TestReport>()
             .HasIndex(r => new { r.DiskCardId, r.TestDate });

         // Foreign key relationships
         modelBuilder.Entity<TestReport>()
             .HasOne(r => r.DiskCard)
             .WithMany(d => d.TestReports)
             .HasForeignKey(r => r.DiskCardId)
             .OnDelete(DeleteBehavior.Cascade);

         modelBuilder.Entity<DriveRecord>()
             .HasIndex(d => d.SerialNumber)
             .IsUnique();

         modelBuilder.Entity<TestRecord>()
             .HasIndex(t => t.DriveId)
             .IsUnique(false);

         modelBuilder.Entity<TestRecord>()
             .HasIndex(t => t.TestDate);

         modelBuilder.Entity<TestRecord>()
             .HasIndex(t => new { t.IsCompleted, t.IsArchived, t.TestType });

         modelBuilder.Entity<TestRecord>()
             .HasOne(t => t.SmartaData)
             .WithOne(s => s.Test)
             .HasForeignKey<SmartaRecord>(s => s.TestId)
             .OnDelete(DeleteBehavior.Cascade);

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

   /// <summary>
   /// Database record for disk/drive information.
   /// </summary>
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

   /// <summary>
   /// Database record for SMART data.
   /// </summary>
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

   /// <summary>
   /// Database record for test results.
   /// </summary>
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
      public long TotalBytesRead { get; set; }
      public long TotalBytesTested { get; set; }
      public int ErrorCount { get; set; }
      public int Errors { get; set; }
      public bool IsCompleted { get; set; }
      public bool IsArchived { get; set; }
      public double HealthScore { get; set; }
      public string Grade { get; set; } = string.Empty;
      public int Score { get; set; }
      public string? CertificatePath { get; set; }
      public string? SurfaceProfile { get; set; }
      public string? SurfaceOperation { get; set; }
      public string? SurfaceTechnology { get; set; }
      public bool SecureErasePerformed { get; set; }

      public DriveRecord Drive { get; set; } = null!;
      public SmartaRecord? SmartaData { get; set; }
      public ICollection<SurfaceTestSampleRecord> SurfaceSamples { get; set; } = new List<SurfaceTestSampleRecord>();
   }

   /// <summary>
   /// Database record for surface test samples.
   /// </summary>
   public class SurfaceTestSampleRecord
   {
      public Guid Id { get; set; }
      public Guid TestId { get; set; }
      public double ThroughputMbps { get; set; }
      public int Temperature { get; set; }
      public int ErrorCount { get; set; }
      public long BytesProcessed { get; set; }
      public long OffsetBytes { get; set; }
      public long BlockSizeBytes { get; set; }
      public DateTime? TimestampUtc { get; set; }

      public TestRecord Test { get; set; } = null!;
   }

   /// <summary>
   /// Database record for email settings.
   /// </summary>
   public class EmailSettingsRecord
   {
      public Guid Id { get; set; }
      public string SmtpServer { get; set; } = string.Empty;
      public string Host { get; set; } = string.Empty;
      public int SmtpPort { get; set; }
      public int Port { get; set; }
      public string FromAddress { get; set; } = string.Empty;
   public string UserName { get; set; } = string.Empty;
      public string Password { get; set; } = string.Empty;
      public bool EnableSsl { get; set; }
      public bool UseSsl { get; set; }
      public string? FromName { get; set; }
      public DateTime? UpdatedAtUtc { get; set; }
   }

   /// <summary>
   /// Database record for replication queue.
   /// </summary>
   public class ReplicationQueueRecord
   {
      public Guid Id { get; set; }
      public Guid TestId { get; set; }
      public DateTime CreatedAt { get; set; }
      public DateTime? ProcessedAt { get; set; }
      public string Status { get; set; } = "Pending";
      public string? ErrorMessage { get; set; }
   }
}
