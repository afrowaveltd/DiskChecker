using System.ComponentModel.DataAnnotations;

namespace DiskChecker.Infrastructure.Persistence;

/// <summary>
/// Database record for disk/drive information.
/// </summary>
public class DriveRecord
{
    public Guid Id { get; set; }
    
    [MaxLength(500)]
    public string Path { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string ModelFamily { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string DeviceModel { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string SerialNumber { get; set; } = string.Empty;
    
    [MaxLength(50)]
    public string FirmwareVersion { get; set; } = string.Empty;
    
    [MaxLength(50)]
    public string FileSystem { get; set; } = string.Empty;
    
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public int TotalTests { get; set; }

    public ICollection<TestRecord> Tests { get; set; } = new List<TestRecord>();
}