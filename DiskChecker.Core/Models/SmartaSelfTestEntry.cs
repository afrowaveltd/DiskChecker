
namespace DiskChecker.Core.Models
{
    public class SmartaSelfTestEntry
    {
        public int Number { get; set; }
        public SmartaSelfTestType Type { get; set; }
        public SmartaSelfTestStatus Status { get; set; }
        public int? RemainingPercent { get; set; }
        public int? LifeTimeHours { get; set; }
        public long? LbaOfFirstError { get; set; }
        public string? CompletedAt { get; set; }
        public SmartaSelfTestType TestType { get; set; }
    }
}
