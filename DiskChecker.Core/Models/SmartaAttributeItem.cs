
namespace DiskChecker.Core.Models
{
    public class SmartaAttributeItem
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int? Value { get; set; }
        public int? Current { get; set; }
        public int? Worst { get; set; }
        public int? Threshold { get; set; }
        public long? RawValue { get; set; }
        public bool IsOk { get; set; }
        public string? WhenFailed { get; set; }
    }
}
