using Microsoft.EntityFrameworkCore;
using DiskChecker.Infrastructure.Persistence;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// Entity Framework database context for DiskChecker.
/// </summary>
public class DiskCheckerDbContext : DbContext
{
    public DiskCheckerDbContext(DbContextOptions<DiskCheckerDbContext> options)
        : base(options)
    {
    }

    // Existing tables
    public DbSet<DriveRecord> DriveRecords { get; set; } = null!;
    public DbSet<TestRecord> TestRecords { get; set; } = null!;
    public DbSet<SmartaRecord> SmartaRecords { get; set; } = null!;
    public DbSet<SurfaceTestSampleRecord> SurfaceTestSamples { get; set; } = null!;
    public DbSet<EmailSettingsRecord> EmailSettings { get; set; } = null!;
    public DbSet<ReplicationQueueRecord> ReplicationQueue { get; set; } = null!;

    // New tables for disk cards and testing
    public DbSet<DiskCard> DiskCards { get; set; } = null!;
    public DbSet<TestSession> TestSessions { get; set; } = null!;
    public DbSet<DiskCertificate> DiskCertificates { get; set; } = null!;
    public DbSet<DiskArchive> DiskArchives { get; set; } = null!;
    public DbSet<SeekSampleRecord> SeekSamples { get; set; } = null!;
    public DbSet<TestTelemetrySample> TestTelemetrySamples { get; set; } = null!;
    public DbSet<TestAnomalyEvent> TestAnomalyEvents { get; set; } = null!;
    public DbSet<TestStallEvent> TestStallEvents { get; set; } = null!;
    public DbSet<SmartSnapshotRecord> SmartSnapshots { get; set; } = null!;

    // Legacy aliases for backward compatibility
    public DbSet<DriveRecord> Drives => DriveRecords;
    public DbSet<TestRecord> Tests => TestRecords;
    public DbSet<SmartaRecord> SmartaData => SmartaRecords;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Existing configurations
        modelBuilder.Entity<DriveRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.Path).HasMaxLength(512);
            entity.HasIndex(e => e.Path).IsUnique();
        });

        modelBuilder.Entity<TestRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // New configurations

        // DiskCard - medical record card for disk
        modelBuilder.Entity<DiskCard>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.ModelName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.SerialNumber).HasMaxLength(128).IsRequired();
            entity.Property(e => e.DevicePath).HasMaxLength(512);
            entity.Property(e => e.DiskType).HasMaxLength(32);
            entity.Property(e => e.InterfaceType).HasMaxLength(32);
            entity.Property(e => e.FirmwareVersion).HasMaxLength(64);
            entity.Property(e => e.ConnectionType).HasMaxLength(32);
            entity.Property(e => e.OverallGrade).HasMaxLength(1);
            entity.Property(e => e.ArchiveReason).HasMaxLength(256);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.LockReason).HasMaxLength(256);

            entity.HasIndex(e => e.SerialNumber).IsUnique();
            entity.HasIndex(e => e.DevicePath);
            entity.HasIndex(e => e.IsArchived);
            entity.HasIndex(e => e.OverallScore);

            // LatestSmartData is runtime data, not stored in DB
            entity.Ignore(e => e.LatestSmartData);
            entity.Ignore(e => e.Volumes);

            entity.HasMany(e => e.TestSessions)
                .WithOne(t => t.DiskCard)
                .HasForeignKey(t => t.DiskCardId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Certificates)
                .WithOne(c => c.DiskCard)
                .HasForeignKey(c => c.DiskCardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // TestSession - individual test run
        modelBuilder.Entity<TestSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.SessionId).IsRequired();
            entity.Property(e => e.PartitionScheme).HasMaxLength(32);
            entity.Property(e => e.FileSystem).HasMaxLength(32);
            entity.Property(e => e.VolumeLabel).HasMaxLength(256);
            entity.Property(e => e.ChartImagePath).HasMaxLength(512);
            entity.Property(e => e.Grade).HasMaxLength(1);
            entity.Property(e => e.Notes).HasMaxLength(2000);

            entity.Property(e => e.SmartBeforeJson).HasColumnType("TEXT");
            entity.Property(e => e.SmartAfterJson).HasColumnType("TEXT");

            // JSON columns for test results and anomaly detection
            entity.Property(e => e.SeekResultsJson).HasColumnType("TEXT");
            entity.Property(e => e.Sanitize1ResultJson).HasColumnType("TEXT");
            entity.Property(e => e.Sanitize2ResultJson).HasColumnType("TEXT");
            entity.Property(e => e.AnomaliesJson).HasColumnType("TEXT");

            entity.HasIndex(e => e.SessionId).IsUnique();
            entity.HasIndex(e => e.DiskCardId);
            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => e.Result);
            entity.HasIndex(e => e.HealthAssessment);

            entity.OwnsMany(e => e.TemperatureSamples, samples =>
            {
                samples.WithOwner().HasForeignKey("TestSessionId");
                samples.Property<int>("Id").ValueGeneratedOnAdd();
                samples.HasKey("Id");
                samples.Property(s => s.Phase).HasMaxLength(32);
            });

            entity.OwnsMany(e => e.WriteSamples, samples =>
            {
                samples.WithOwner().HasForeignKey("TestSessionId");
                samples.Property<int>("Id").ValueGeneratedOnAdd();
                samples.HasKey("Id");
            });

            entity.OwnsMany(e => e.ReadSamples, samples =>
            {
                samples.WithOwner().HasForeignKey("TestSessionId");
                samples.Property<int>("Id").ValueGeneratedOnAdd();
                samples.HasKey("Id");
            });

            entity.OwnsMany(e => e.SmartChanges, changes =>
            {
                changes.WithOwner().HasForeignKey("TestSessionId");
                changes.Property<int>("Id").ValueGeneratedOnAdd();
                changes.HasKey("Id");
                changes.Property(c => c.AttributeName).HasMaxLength(128);
                changes.Property(c => c.Warning).HasMaxLength(512);
            });

            entity.OwnsMany(e => e.Errors, errors =>
            {
                errors.WithOwner().HasForeignKey("TestSessionId");
                errors.Property<int>("Id").ValueGeneratedOnAdd();
                errors.HasKey("Id");
                errors.Property(e => e.ErrorCode).HasMaxLength(32);
                errors.Property(e => e.Message).HasMaxLength(1024);
                errors.Property(e => e.Phase).HasMaxLength(32);
                errors.Property(e => e.Details).HasMaxLength(2000);
            });
        });

        // DiskCertificate - generated certificate
        modelBuilder.Entity<DiskCertificate>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.CertificateNumber).HasMaxLength(64).IsRequired();
            entity.Property(e => e.GeneratedBy).HasMaxLength(256);
            entity.Property(e => e.DiskModel).HasMaxLength(256);
            entity.Property(e => e.SerialNumber).HasMaxLength(128);
            entity.Property(e => e.Capacity).HasMaxLength(32);
            entity.Property(e => e.DiskType).HasMaxLength(32);
            entity.Property(e => e.Firmware).HasMaxLength(64);
            entity.Property(e => e.Interface).HasMaxLength(32);
            entity.Property(e => e.TestType).HasMaxLength(64);
            entity.Property(e => e.Grade).HasMaxLength(1);
            entity.Property(e => e.HealthStatus).HasMaxLength(64);
            entity.Property(e => e.TemperatureRange).HasMaxLength(32);
            entity.Property(e => e.SanitizationMethod).HasMaxLength(64);
            entity.Property(e => e.PartitionScheme).HasMaxLength(32);
            entity.Property(e => e.FileSystem).HasMaxLength(32);
            entity.Property(e => e.VolumeLabel).HasMaxLength(256);
            entity.Property(e => e.PdfPath).HasMaxLength(512);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.RecommendationNotes).HasMaxLength(1024);

            entity.HasIndex(e => e.CertificateNumber).IsUnique();
            entity.HasIndex(e => e.DiskCardId);
            entity.HasIndex(e => e.GeneratedAt);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.TestSession)
                .WithMany()
                .HasForeignKey(e => e.TestSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.OwnsMany(e => e.SmartAttributes, attrs =>
            {
                attrs.WithOwner().HasForeignKey("DiskCertificateId");
                attrs.Property<int>("Id").ValueGeneratedOnAdd();
                attrs.HasKey("Id");
                attrs.Property(a => a.Name).HasMaxLength(128);
                attrs.Property(a => a.Value).HasMaxLength(64);
                attrs.Property(a => a.Status).HasMaxLength(32);
            });
        });



        // Research/analysis throughput telemetry. This supersedes owned WriteSamples/ReadSamples
        // for future detailed analysis while keeping the old collections functional for current UI.
        modelBuilder.Entity<TestTelemetrySample>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RetentionReason).HasMaxLength(64);
            entity.HasIndex(e => e.TestSessionId);
            entity.HasIndex(e => new { e.TestSessionId, e.Phase, e.SequenceIndex }).IsUnique();
            entity.HasIndex(e => new { e.TestSessionId, e.Phase, e.TimestampUtc });
            entity.HasIndex(e => new { e.TestSessionId, e.Phase, e.ProgressPercent });
            entity.HasIndex(e => new { e.TestSessionId, e.IsStalled });
            entity.HasIndex(e => new { e.TestSessionId, e.IsAnomaly });

            entity.HasOne(e => e.TestSession)
                .WithMany()
                .HasForeignKey(e => e.TestSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        modelBuilder.Entity<TestAnomalyEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OverlayGroup).HasMaxLength(128);
            entity.Property(e => e.DefectType).HasMaxLength(128);
            entity.HasIndex(e => e.TestSessionId);
            entity.HasIndex(e => new { e.TestSessionId, e.Phase, e.StartProgressPercent });
            entity.HasIndex(e => new { e.TestSessionId, e.Phase, e.StartLba512 });
            entity.HasIndex(e => new { e.TestSessionId, e.SeverityScore });

            entity.HasOne(e => e.TestSession)
                .WithMany()
                .HasForeignKey(e => e.TestSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        modelBuilder.Entity<TestStallEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TestSessionId);
            entity.HasIndex(e => new { e.TestSessionId, e.Phase, e.StartedAtUtc });
            entity.HasIndex(e => new { e.TestSessionId, e.Phase, e.StartProgressPercent });

            entity.HasOne(e => e.TestSession)
                .WithMany()
                .HasForeignKey(e => e.TestSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Complete seek-test sample persistence. Unlike throughput samples these are
        // intentionally not reduced because seek tests are bounded (usually <= 3000).
        modelBuilder.Entity<SeekSampleRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1024);
            entity.HasIndex(e => e.TestSessionId);
            entity.HasIndex(e => new { e.TestSessionId, e.TestType, e.Index }).IsUnique();
            entity.HasIndex(e => new { e.TestSessionId, e.TimestampUtc });
            entity.HasIndex(e => new { e.TestSessionId, e.SeekDistance });

            entity.HasOne(e => e.TestSession)
                .WithMany()
                .HasForeignKey(e => e.TestSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // DiskArchive - archived disks
        modelBuilder.Entity<DiskArchive>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.ArchivedBy).HasMaxLength(256);
            entity.Property(e => e.Summary).HasMaxLength(512);
            entity.Property(e => e.FinalGrade).HasMaxLength(1);
            entity.Property(e => e.Notes).HasMaxLength(2000);

            entity.HasIndex(e => e.DiskCardId);
            entity.HasIndex(e => e.ArchivedAt);
            entity.HasIndex(e => e.Reason);
        });

        // SmartSnapshotRecord - historical SMART snapshots for trend analysis
        modelBuilder.Entity<SmartSnapshotRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.RetrievedAtUtc).IsRequired();

            entity.HasIndex(e => e.DiskCardId);
            entity.HasIndex(e => e.TestSessionId);
            entity.HasIndex(e => e.RetrievedAtUtc);
            entity.HasIndex(e => new { e.DiskCardId, e.RetrievedAtUtc });
            entity.HasIndex(e => new { e.DiskCardId, e.PowerOnHours });
            entity.HasIndex(e => new { e.DiskCardId, e.ReallocatedSectorCount });
            entity.HasIndex(e => new { e.DiskCardId, e.PercentageUsed });

            entity.HasOne(e => e.DiskCard)
                .WithMany()
                .HasForeignKey(e => e.DiskCardId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.TestSession)
                .WithMany()
                .HasForeignKey(e => e.TestSessionId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
