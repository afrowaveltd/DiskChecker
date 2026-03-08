using Microsoft.EntityFrameworkCore;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Persistence;

public class DiskCheckerDbContext : DbContext
{
    public DbSet<HistoricalTest> HistoricalTests => Set<HistoricalTest>();
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();
    public DbSet<DiskBenchmark> DiskBenchmarks => Set<DiskBenchmark>();
    
    public DiskCheckerDbContext(DbContextOptions<DiskCheckerDbContext> options) : base(options)
    {
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
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
        
        // EmailSettings - uses Host + FromAddress as key (no Id)
        modelBuilder.Entity<EmailSettings>(entity =>
        {
            entity.HasKey(e => new { e.Host, e.FromAddress });
            entity.Property(e => e.Host).HasMaxLength(200);
            entity.Property(e => e.UserName).HasMaxLength(200);
            entity.Property(e => e.Password).HasMaxLength(500);
            entity.Property(e => e.FromName).HasMaxLength(100);
            entity.Property(e => e.FromAddress).HasMaxLength(200);
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
    }
}
