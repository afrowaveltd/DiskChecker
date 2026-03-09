namespace DiskChecker.Core.Models {
      public class SpeedSample {
          public long OffsetBytes { get; set; }
          public int BlockSizeBytes { get; set; }
          public double ThroughputMbps { get; set; }
          public DateTime TimestampUtc { get; set; }
          public int ErrorCount { get; set; }
      }
    }