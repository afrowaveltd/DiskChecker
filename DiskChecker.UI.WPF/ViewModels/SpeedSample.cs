namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// Represents a single speed sample point in the real-time graph.
/// </summary>
public class SpeedSample
{
   /// <summary>
   /// Time of the sample (relative seconds).
   /// </summary>
   public double TimeSeconds { get; set; }

   /// <summary>
   /// Throughput in MB/s.
   /// </summary>
   public double ThroughputMbps { get; set; }

   /// <summary>
   /// Is this a write or read sample (0=write, 1=read).
   /// </summary>
   public int Phase { get; set; }
}
