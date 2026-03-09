
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Models
{
    public class SmartCheckResult
    {
        public string? Drive { get; set; }
        public SmartaData? SmartaData { get; set; }
        public QualityRating? Rating { get; set; }
        public DateTime? TestDate { get; set; }
        public string? TestId { get; set; }
        public SmartaSelfTestStatus? SelfTestStatus { get; set; }
        public List<SmartaSelfTestEntry>? SelfTestLog { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsHealthy { get; set; }
        public bool TestPassed { get; set; }
        public int? PowerOnHours { get; set; }
        public int? ReallocatedSectorCount { get; set; }
        public int? PendingSectorCount { get; set; }
        public int? UncorrectableErrorCount { get; set; }
        public int? Temperature { get; set; }
        public int? WearLevelingCount { get; set; }
        public List<SmartaAttributeItem> Attributes { get; set; } = new List<SmartaAttributeItem>();
        public List<SmartaSelfTestEntry> SelfTests { get; set; } = new List<SmartaSelfTestEntry>();
        public SmartaSelfTestEntry? CurrentSelfTest { get; set; }
    }
}
