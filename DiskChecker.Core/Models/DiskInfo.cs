namespace DiskChecker.Core.Models;

/// <summary>
/// Represents basic disk drive information.
/// </summary>
public class DiskInfo
{
    /// <summary>
    /// Gets or sets the drive letter (e.g., "C:").
    /// </summary>
    public string? DriveLetter { get; set; }

    /// <summary>
    /// Gets or sets the device path (e.g., "/dev/sda" on Linux).
    /// </summary>
    public string? DevicePath { get; set; }

    /// <summary>
    /// Gets or sets the disk model name.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the disk serial number.
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Gets or sets the disk manufacturer.
    /// </summary>
    public string? Manufacturer { get; set; }

    /// <summary>
    /// Gets or sets the interface type (SATA, NVMe, USB, etc.).
    /// </summary>
    public string? InterfaceType { get; set; }

    /// <summary>
    /// Gets or sets the total size in bytes.
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the allocated size in bytes.
    /// </summary>
    public long AllocatedSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the free space in bytes.
    /// </summary>
    public long FreeSpaceBytes { get; set; }

    /// <summary>
    /// Gets or sets the media type (Fixed, Removable, etc.).
    /// </summary>
    public string? MediaType { get; set; }

    /// <summary>
    /// Gets or sets the file system (NTFS, ext4, etc.).
    /// </summary>
    public string? FileSystem { get; set; }

    /// <summary>
    /// Gets or sets whether the disk is ready.
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>
    /// Gets or sets the drive type (Unknown, NoRootDirectory, Removable, Local, Network, CDRom).
    /// </summary>
    public string DriveType { get; set; } = "Unknown";

    /// <summary>
    /// Gets or sets volume label.
    /// </summary>
    public string? VolumeLabel { get; set; }

    /// <summary>
    /// Gets or sets the RPM for HDD drives.
    /// </summary>
    public int? Rpm { get; set; }

    /// <summary>
    /// Gets or sets the SMART health status.
    /// </summary>
    public string? SmartHealthStatus { get; set; }

    /// <summary>
    /// Gets or sets whether SMART is supported.
    /// </summary>
    public bool IsSmartSupported { get; set; }

    /// <summary>
    /// Gets or sets whether SMART is enabled.
    /// </summary>
    public bool IsSmartEnabled { get; set; }
}
