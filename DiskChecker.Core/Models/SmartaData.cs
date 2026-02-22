namespace DiskChecker.Core.Models;

public class SmartaData
{
    public string? ModelFamily { get; set; }
    public string? DeviceModel { get; set; }
    public string? SerialNumber { get; set; }
    public string? FirmwareVersion { get; set; }
    public int PowerOnHours { get; set; }
    public long ReallocatedSectorCount { get; set; }
    public long PendingSectorCount { get; set; }
    public long UncorrectableErrorCount { get; set; }
    public double Temperature { get; set; }
    public int? WearLevelingCount { get; set; }
    public DateTime? LastChecked { get; set; }
}

public class CoreDriveInfo
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public string FileSystem { get; set; } = string.Empty;
}

public enum QualityGrade
{
    A,
    B,
    C,
    D,
    E,
    F
}

public class QualityRating
{
    public QualityGrade Grade { get; set; } = QualityGrade.C;
    public double Score { get; set; }
    public List<string> Warnings { get; set; } = new();
}
