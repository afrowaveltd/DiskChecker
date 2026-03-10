namespace DiskChecker.Core.Models
{
    /// <summary>
    /// Result of SMART check for a drive. Supports ATA/SATA, NVMe, and SCSI/SAS drives.
    /// </summary>
    public class SmartCheckResult
    {
        // Drive identification
        public string? Drive { get; set; }
        public string? DeviceModel { get; set; }
        public string? SerialNumber { get; set; }
        public string? FirmwareVersion { get; set; }
        public string? DeviceType { get; set; }
        
        // Associated SmartaData (for compatibility)
        public SmartaData? SmartaData { get; set; }
        
        // Quality rating
        public QualityRating? Rating { get; set; }
        
        // Test metadata
        public DateTime? TestDate { get; set; }
        public string? TestId { get; set; }
        
        // SMART status
        public bool IsHealthy { get; set; }
        public bool IsEnabled { get; set; }
        public bool TestPassed { get; set; }
        public SmartaSelfTestStatus? SelfTestStatus { get; set; }
        
        // Basic metrics
        public int? Temperature { get; set; }
        public int? PowerOnHours { get; set; }
        public int PowerCycleCount { get; set; }
        public long TotalSize { get; set; }
        
        // ATA/SATA specific
        public int? ReallocatedSectorCount { get; set; }
        public int? PendingSectorCount { get; set; }
        public int? UncorrectableErrorCount { get; set; }
        public int? WearLevelingCount { get; set; }
        
        // NVMe specific
        public int? AvailableSparePercent { get; set; }
        public int? EnduranceUsedPercent { get; set; }
        public int? MediaErrors { get; set; }
        public int? UnsafeShutdowns { get; set; }
        
        // Attributes and self-tests
        public List<SmartaAttributeItem> Attributes { get; set; } = new();
        public List<SmartaSelfTestEntry> SelfTests { get; set; } = new();
        public List<SmartaSelfTestEntry>? SelfTestLog { get; set; }
        public SmartaSelfTestEntry? CurrentSelfTest { get; set; }
    }
}