namespace DiskChecker.UI.Avalonia.ViewModels;

public class SmartHistoryItem
{
    public DateTime TestedAt { get; set; }
    public int Temperature { get; set; }
    public int PowerOnHours { get; set; }
    public int ReallocatedSectors { get; set; }
    public int PendingSectors { get; set; }
    public int ReadErrors { get; set; }
    public int ReallocEvents { get; set; }
    public string Grade { get; set; } = "?";
    public double Score { get; set; }
    public string Notes { get; set; } = string.Empty;
}