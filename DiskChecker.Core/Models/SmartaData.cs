using System.Collections.Generic;

namespace DiskChecker.Core.Models
{
    public class SmartaData
    {
        public string? ModelFamily { get; set; }
        public string? DeviceModel { get; set; }
        public string? SerialNumber { get; set; }
        public string? FirmwareVersion { get; set; }
        public int? PowerOnHours { get; set; }
        public int? ReallocatedSectorCount { get; set; }
        public int? PendingSectorCount { get; set; }
        public int? UncorrectableErrorCount { get; set; }
        public int? Temperature { get; set; }
        public int? WearLevelingCount { get; set; }
        public List<SmartaAttributeItem> Attributes { get; set; } = new List<SmartaAttributeItem>();
        public List<SmartaSelfTestEntry> SelfTests { get; set; } = new List<SmartaSelfTestEntry>();
    }
}