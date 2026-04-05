using System.ComponentModel.DataAnnotations;

namespace DiskChecker.Core.Models;

/// <summary>
/// Core information about a disk/drive detected by the system.
/// </summary>
public class CoreDriveInfo
{
    /// <summary>
    /// Unique identifier for this drive instance.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Drive path (e.g., /dev/sda, C:\, \\.\PhysicalDrive0).
    /// </summary>
    [MaxLength(500)]
    public string Path { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable drive name (e.g., "Samsung SSD 850 EVO 250GB").
    /// </summary>
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Total size of the drive in bytes.
    /// </summary>
    public long TotalSize { get; set; }
    
    /// <summary>
    /// Available free space in bytes (if applicable).
    /// </summary>
    public long FreeSpace { get; set; }
    
    /// <summary>
    /// File system type (e.g., "NTFS", "ext4", "FAT32").
    /// </summary>
    [MaxLength(50)]
    public string FileSystem { get; set; } = string.Empty;
    
    /// <summary>
    /// Drive interface type (e.g., "SATA", "NVMe", "USB").
    /// </summary>
    [MaxLength(50)]
    public string Interface { get; set; } = string.Empty;
    
    /// <summary>
    /// Serial number of the drive.
    /// </summary>
    [MaxLength(100)]
    public string? SerialNumber { get; set; }
    
    /// <summary>
    /// Manufacturer model name.
    /// </summary>
    [MaxLength(200)]
    public string? Model { get; set; }
    
    /// <summary>
    /// Firmware version string.
    /// </summary>
    [MaxLength(50)]
    public string? FirmwareVersion { get; set; }
    
    /// <summary>
    /// Whether this is a physical drive or a logical/partition.
    /// </summary>
    public bool IsPhysical { get; set; }
    
    /// <summary>
    /// Whether the drive is removable.
    /// </summary>
    public bool IsRemovable { get; set; }
    
    /// <summary>
    /// Whether the drive is ready for I/O operations.
    /// </summary>
    public bool IsReady { get; set; }
    
    /// <summary>
    /// Additional metadata about the drive.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Logical volumes (partitions) that belong to this physical drive.
    /// Populated when logical drives are associated with physical devices.
    /// </summary>
    public List<CoreDriveInfo> Volumes { get; set; } = new();
    
    /// <summary>
    /// Media type of the disk (e.g., "Fixed hard disk media", "Removable").
    /// </summary>
    public string? MediaType { get; set; }
    
    /// <summary>
    /// Indicates whether the drive is detected as solid-state (SSD/NVMe).
    /// </summary>
    public bool? IsSolidState { get; set; }

    /// <summary>
    /// Indicates whether the drive is detected as rotational (HDD spindle based).
    /// </summary>
    public bool? IsRotational { get; set; }

    /// <summary>
    /// Bus/connection type for this disk.
    /// </summary>
    public CoreBusType BusType { get; set; } = CoreBusType.Unknown;
    
    /// <summary>
    /// Firmware revision string (alias for FirmwareVersion).
    /// </summary>
    public string? FirmwareRevision 
    { 
        get => FirmwareVersion; 
        set => FirmwareVersion = value; 
    }
    
    // Legacy compatibility properties
    public string? VolumeInfo { get; set; }
    public bool IsSystemDisk { get; set; }
}