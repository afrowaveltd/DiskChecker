namespace DiskChecker.Core.Models;

/// <summary>
/// Represents the bus/connection type for a disk.
/// </summary>
public enum CoreBusType
{
    /// <summary>
    /// Unknown bus type.
    /// </summary>
    Unknown = 0,
    
    /// <summary>
    /// SCSI connection.
    /// </summary>
    Scsi = 1,
    
    /// <summary>
    /// ATA/IDE connection.
    /// </summary>
    Ata = 2,
    
    /// <summary>
    /// SATA connection.
    /// </summary>
    Sata = 3,
    
    /// <summary>
    /// NVMe connection.
    /// </summary>
    Nvme = 4,
    
    /// <summary>
    /// USB connection.
    /// </summary>
    Usb = 5,
    
    /// <summary>
    /// FireWire connection.
    /// </summary>
    FireWire = 6,
    
    /// <summary>
    /// SD card.
    /// </summary>
    Sd = 7,
    
    /// <summary>
    /// MMC card.
    /// </summary>
    Mmc = 8,
    
    /// <summary>
    /// Virtual/ramdisk.
    /// </summary>
    Virtual = 9,
    
    /// <summary>
    /// RAID array.
    /// </summary>
    Raid = 10,
    
    /// <summary>
    /// File-backed virtual.
    /// </summary>
    FileBackedVirtual = 11,
    
    /// <summary>
    /// IDE connection (legacy PATA).
    /// </summary>
    Ide = 12,
    
    /// <summary>
    /// SAS connection.
    /// </summary>
    Sas = 13
}

/// <summary>
/// Extension methods for CoreBusType to provide user-friendly display names.
/// </summary>
public static class CoreBusTypeExtensions
{
    /// <summary>
    /// Gets the display name for the bus type.
    /// </summary>
    public static string GetDisplayName(this CoreBusType busType)
    {
        return busType switch
        {
            CoreBusType.Nvme => "NVMe",
            CoreBusType.Sata => "SATA",
            CoreBusType.Usb => "USB",
            CoreBusType.Sas => "SAS",
            CoreBusType.Ide => "IDE",
            CoreBusType.Scsi => "SCSI",
            CoreBusType.Ata => "ATA",
            CoreBusType.FireWire => "FireWire",
            CoreBusType.Sd => "SD",
            CoreBusType.Mmc => "MMC",
            CoreBusType.Virtual => "Virtual",
            CoreBusType.Raid => "RAID",
            CoreBusType.FileBackedVirtual => "Virtual",
            _ => "Unknown"
        };
    }
}