using DiskChecker.TUI.Models;
using Microsoft.EntityFrameworkCore;

namespace DiskChecker.TUI.Data;

/// <summary>
/// EF Core database context for the TUI disk checker.
/// Uses SQLite – same schema concept as the main DiskChecker for data portability.
/// </summary>
public sealed class TuiDbContext : DbContext
{
    public DbSet<TuiDiskRecord> Disks { get; set; } = null!;
    public DbSet<TuiTestSession> TestSessions { get; set; } = null!;
    public DbSet<TuiSpeedSample> SpeedSamples { get; set; } = null!;

    private readonly string _dbPath;

    public TuiDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TuiDiskRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SerialNumber);
            entity.HasIndex(e => e.DevicePath);
            entity.HasMany(e => e.TestSessions)
                  .WithOne()
                  .HasForeignKey(e => e.DiskRecordId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TuiTestSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StartedAt);
            entity.HasMany(e => e.WriteSamples)
                  .WithOne()
                  .HasForeignKey(e => e.TestSessionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.ReadSamples)
                  .WithOne()
                  .HasForeignKey(e => e.TestSessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TuiSpeedSample>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SpeedMBps).HasColumnType("REAL");
            entity.Property(e => e.PositionPercent).HasColumnType("REAL");
        });
    }
}

/// <summary>
/// Persistent disk record in the local SQLite database.
/// </summary>
public sealed class TuiDiskRecord
{
    public long Id { get; set; }
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string DevicePath { get; set; } = string.Empty;
    public string FirmwareRevision { get; set; } = string.Empty;
    public ulong CapacityBytes { get; set; }
    public string InterfaceType { get; set; } = string.Empty;
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastTested { get; set; }

    public List<TuiTestSession> TestSessions { get; set; } = new();
}

/// <summary>
/// Persistent test session record.
/// </summary>
public sealed class TuiTestSession
{
    public long Id { get; set; }
    public long DiskRecordId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public string TestType { get; set; } = string.Empty; // "FullDestructive", "WriteOnly", "ReadOnly", "SeekOnly", "SanitizeOnly"

    public double WriteSpeedAvgMBps { get; set; }
    public double WriteSpeedMinMBps { get; set; }
    public double WriteSpeedMaxMBps { get; set; }

    public double ReadSpeedAvgMBps { get; set; }
    public double ReadSpeedMinMBps { get; set; }
    public double ReadSpeedMaxMBps { get; set; }

    public double? SeekAvgMs { get; set; }
    public double? SeekMinMs { get; set; }
    public double? SeekMaxMs { get; set; }

    public double? MaxTemperatureC { get; set; }
    public double? AvgTemperatureC { get; set; }

    public bool SanitizationPassed { get; set; }
    public string? SanitizationMethod { get; set; }
    public string? SanitizationOutput { get; set; }

    public string? ErrorMessage { get; set; }
    public string? Grade { get; set; }

    public List<TuiSpeedSample> WriteSamples { get; set; } = new();
    public List<TuiSpeedSample> ReadSamples { get; set; } = new();
}

/// <summary>
/// Persistent speed sample for graph reconstruction.
/// </summary>
public sealed class TuiSpeedSample
{
    public long Id { get; set; }
    public long TestSessionId { get; set; }
    public bool IsWrite { get; set; } // true = write, false = read
    public double PositionPercent { get; set; }
    public double SpeedMBps { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
