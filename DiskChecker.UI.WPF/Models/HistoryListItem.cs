namespace DiskChecker.UI.WPF.ViewModels;

public class HistoryListItem
{
   public Guid TestId { get; set; }
   public DateTime TestDate { get; set; }
   public string DriveName { get; set; } = string.Empty;
   public string SerialNumber { get; set; } = string.Empty;
   public string TestType { get; set; } = string.Empty;
   public string Grade { get; set; } = string.Empty;
   public double Score { get; set; }
   public int ErrorCount { get; set; }
}
