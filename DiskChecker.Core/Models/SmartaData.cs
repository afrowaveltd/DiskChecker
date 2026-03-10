using System.Collections.Generic;

namespace DiskChecker.Core.Models
{
    /// <summary>
    /// Complete SMART data for a drive. Supports ATA/SATA, NVMe, and SCSI/SAS drives.
    /// </summary>
    public class SmartaData
    {
        // Drive identification
        public string? ModelFamily { get; set; }
        public string DeviceModel { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string FirmwareVersion { get; set; } = "";
        public string DeviceType { get; set; } = "Unknown";
        
        // Drive capacity
        public long? TotalSize { get; set; }
        
        // SMART status
        public bool IsHealthy { get; set; }
        public bool SmartEnabled { get; set; }
        
        // Basic metrics (all drive types)
        public int? Temperature { get; set; }
        public int? PowerOnHours { get; set; }
        public int PowerCycleCount { get; set; }
        
        // ATA/SATA specific
        public int? ReallocatedSectorCount { get; set; }
        public int? PendingSectorCount { get; set; }
        public int? UncorrectableErrorCount { get; set; }
        public int? WearLevelingCount { get; set; }
        
        // NVMe specific
        public int? AvailableSpare { get; set; }
        public int? PercentageUsed { get; set; }
        public int? MediaErrors { get; set; }
        public int? UnsafeShutdowns { get; set; }
        
        // Raw attributes
        public List<SmartaAttributeItem> Attributes { get; set; } = new();
        public List<SmartaSelfTestEntry> SelfTests { get; set; } = new();
        
        // Current self-test status
        public bool SelfTestInProgress { get; set; }
        public int? SelfTestProgressPercent { get; set; }
        
        // Computed properties for display
        public string TemperatureDisplay => Temperature > 0 ? $"{Temperature}°C" : "N/A";
        public string PowerOnHoursDisplay => PowerOnHours > 0 ? $"{PowerOnHours:N0} h" : "N/A";
        public string PowerCycleCountDisplay => PowerCycleCount > 0 ? $"{PowerCycleCount:N0}" : "N/A";
        public string HealthStatus => IsHealthy ? "✅ Zdravý" : "⚠️ Pozor";
        
        public string TemperatureStatus
        {
            get
            {
                if (Temperature == null || Temperature == 0) return "N/A";
                if (Temperature < 35) return "❄️ Studený";
                if (Temperature < 50) return "✅ Normální";
                if (Temperature < 60) return "⚠️ Teplý";
                if (Temperature < 70) return "🔥 Horký";
                return "🔥🔥 Přehřívání!";
            }
        }
        
        public string LifetimeStatus
        {
            get
            {
                if (PowerOnHours == null || PowerOnHours == 0) return "N/A";
                var years = PowerOnHours.Value / (365.25 * 24);
                if (years < 1) return "Nový (< 1 rok)";
                if (years < 3) return "Mladý (1-3 roky)";
                if (years < 5) return "Středně starý (3-5 let)";
                return $"Starý ({years:F1} let)";
            }
        }
    }
}