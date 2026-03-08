namespace DiskChecker.Core.Models;

/// <summary>
/// Represents a disk benchmark result stored in the database.
/// </summary>
public class DiskBenchmark
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Gets or sets the disk serial number.
    /// </summary>
    public string? SerialNumber { get; set; }
    
    /// <summary>
    /// Gets or sets the disk model.
    /// </summary>
    public string? Model { get; set; }
    
    /// <summary>
    /// Gets or sets the benchmark date.
    /// </summary>
    public DateTime BenchmarkDate { get; set; }
    
    /// <summary>
    /// Gets or sets the sequential read speed in MB/s.
    /// </summary>
    public double SequentialReadSpeed { get; set; }
    
    /// <summary>
    /// Gets or sets the sequential write speed in MB/s.
    /// </summary>
    public double SequentialWriteSpeed { get; set; }
    
    /// <summary>
    /// Gets or sets the random 4K read speed in IOPS.
    /// </summary>
    public double RandomReadIops { get; set; }
    
    /// <summary>
    /// Gets or sets the random 4K write speed in IOPS.
    /// </summary>
    public double RandomWriteIops { get; set; }
    
    /// <summary>
    /// Gets or sets the access time in ms.
    /// </summary>
    public double AccessTime { get; set; }
    
    /// <summary>
    /// Gets or sets the benchmark file path.
    /// </summary>
    public string? FilePath { get; set; }
}
