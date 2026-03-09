using DiskChecker.Core.Models;
using Microsoft.EntityFrameworkCore;
using System;

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
      public DbSet<HistoricalTest> HistoricalTests => Set<HistoricalTest>();
      public DbSet<DiskBenchmark> DiskBenchmarks => Set<DiskBenchmark>();

      protected override void OnModelCreating(ModelBuilder modelBuilder)
      {
         base.OnModelCreating(modelBuilder);
         
         // Ignore transient DTOs used by services/UI so EF Core does not try to map them as entities.
         modelBuilder.Ignore<SmartCheckResult>();

         // HistoricalTest - uses TestId as primary key
         modelBuilder.Entity<HistoricalTest>(entity =>
         {
             entity.HasKey(e => e.TestId);
             entity.Property(e => e.SerialNumber).HasMaxLength(100);
             entity.Property(e => e.Model).HasMaxLength(200);
             entity.Property(e => e.TestType).HasMaxLength(50);
             entity.Property(e => e.Grade).HasMaxLength(10);
             entity.Property(e => e.HealthAssessment).HasMaxLength(200);
             entity.HasIndex(e => e.SerialNumber);
             entity.HasIndex(e => e.TestDate);
         });

         // DiskBenchmark
         modelBuilder.Entity<DiskBenchmark>(entity =>
         {
             entity.HasKey(e => e.Id);
             entity.Property(e => e.SerialNumber).HasMaxLength(100);
             entity.Property(e => e.Model).HasMaxLength(200);
             entity.HasIndex(e => e.SerialNumber);
             entity.HasIndex(e => e.BenchmarkDate);
         });

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
             
         modelBuilder.Entity<EmailSettingsRecord>()
             .HasIndex(e => new { e.Host, e.FromAddress })
             .IsUnique();
      }
   }
}