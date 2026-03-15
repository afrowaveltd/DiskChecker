using System;
using System.Collections.Generic;

namespace DiskChecker.Core.Models;

/// <summary>
/// Medical record card for a disk - stores all tests and data for a specific disk.
/// </summary>
public class DiskCard
{
    public int Id { get; set; }
    
    /// <summary>
    /// Disk model name (e.g., "Samsung SSD 870 EVO 500GB")
    /// </summary>
    public string ModelName { get; set; } = string.Empty;
    
    /// <summary>
    /// Serial number - unique identifier
    /// </summary>
    public string SerialNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Device path (\\.\PhysicalDrive0, /dev/sda, etc.)
    /// </summary>
    public string DevicePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Disk type (SSD, HDD, NVMe)
    /// </summary>
    public string DiskType { get; set; } = string.Empty;
    
    /// <summary>
    /// Interface type (SATA, NVMe, SAS)
    /// </summary>
    public string InterfaceType { get; set; } = string.Empty;
    
    /// <summary>
    /// Capacity in bytes
    /// </summary>
    public long Capacity { get; set; }
    
    /// <summary>
    /// Firmware version
    /// </summary>
    public string FirmwareVersion { get; set; } = string.Empty;
    
    /// <summary>
    /// Connection type (Internal, USB, etc.)
    /// </summary>
    public string ConnectionType { get; set; } = string.Empty;
    
    /// <summary>
    /// When this card was created (first test)
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Last time this disk was tested
    /// </summary>
    public DateTime LastTestedAt { get; set; }
    
    /// <summary>
    /// Overall health grade (A-F)
    /// </summary>
    public string OverallGrade { get; set; } = "?";
    
    /// <summary>
    /// Overall health score (0-100)
    /// </summary>
    public double OverallScore { get; set; }
    
    /// <summary>
    /// Number of test sessions
    /// </summary>
    public int TestCount { get; set; }
    
    /// <summary>
    /// Whether this disk is archived (removed from active testing)
    /// </summary>
    public bool IsArchived { get; set; }
    
    /// <summary>
    /// Reason for archiving (Failed, Sold, Donated, etc.)
    /// </summary>
    public string? ArchiveReason { get; set; }
    
    /// <summary>
    /// Notes about this disk
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Current lock status
    /// </summary>
    public bool IsLocked { get; set; }
    
    /// <summary>
    /// Lock reason (System disk, User locked, etc.)
    /// </summary>
    public string? LockReason { get; set; }
    
    /// <summary>
    /// All test sessions for this disk
    /// </summary>
    public List<TestSession> TestSessions { get; set; } = new();

    /// <summary>
    /// Runtime seznam oddílů a přípojných bodů pro daný fyzický disk (není perzistováno v DB).
    /// </summary>
    public List<CoreDriveInfo> Volumes { get; set; } = new();

    /// <summary>
    /// Latest SMART data snapshot
    /// </summary>
    public SmartaData? LatestSmartData { get; set; }
    
    /// <summary>
    /// All certificates generated for this disk
    /// </summary>
    public List<DiskCertificate> Certificates { get; set; } = new();
}