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

public enum SmartaMaintenanceAction
{
    EnableSmart,
    DisableSmart,
    EnableAutoSave,
    DisableAutoSave,
    RunOfflineDataCollection,
    AbortSelfTest
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

// <summary>
// Represents the result of a SMART check for a single drive.
// </summary>
public class SmartCheckResult
{
    // <summary>
    // Gets or sets the drive information used for the SMART check.
    // </summary>
    public CoreDriveInfo Drive { get; set; } = new();

    // <summary>
    // Gets or sets the captured SMART data snapshot.
    // </summary>
    public SmartaData SmartaData { get; set; } = new();

    // <summary>
    // Gets or sets the calculated quality rating.
    // </summary>
    public QualityRating Rating { get; set; } = new();

    // <summary>
    // Gets or sets the time when the SMART check was executed.
    // </summary>
    public DateTime TestDate { get; set; }

    // <summary>
    // Gets or sets the persisted test identifier.
    // </summary>
    public Guid TestId { get; set; }

    /// <summary>
    /// Gets or sets parsed SMART attributes when available.
    /// </summary>
    public IReadOnlyList<SmartaAttributeItem> Attributes { get; set; } = Array.Empty<SmartaAttributeItem>();

    /// <summary>
    /// Gets or sets current SMART self-test status.
    /// </summary>
    public SmartaSelfTestStatus? SelfTestStatus { get; set; }

    /// <summary>
    /// Gets or sets recent SMART self-test log entries.
    /// </summary>
    public IReadOnlyList<SmartaSelfTestEntry> SelfTestLog { get; set; } = Array.Empty<SmartaSelfTestEntry>();
}

public enum SmartaSelfTestType
{
    Quick,
    Extended,
    Conveyance,
    Selective,
    Offline,
    Abort
}

public class SmartaAttributeItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Current { get; set; }
    public int? Worst { get; set; }
    public int? Threshold { get; set; }
    public long RawValue { get; set; }
    public string? WhenFailed { get; set; }

    /// <summary>
    /// Indicates whether this SMART attribute should be treated as critical in UI.
    /// </summary>
    public bool IsCritical
    {
        get
        {
            if (Current.HasValue && Threshold.HasValue && Current.Value <= Threshold.Value)
            {
                return true;
            }

            if (RawValue <= 0)
            {
                return false;
            }

            return Id is 5 or 197 or 198;
        }
    }
}

public class SmartaSelfTestStatus
{
    public bool IsRunning { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public int? RemainingPercent { get; set; }
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SmartaSelfTestEntry
{
    public int? Number { get; set; }
    public string TestType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? RemainingPercent { get; set; }
    public int? LifeTimeHours { get; set; }
    public long? LbaOfFirstError { get; set; }
}

public class SmartaSelfTestReport
{
    public SmartaSelfTestType RequestedTestType { get; set; }
    public bool Completed { get; set; }
    public bool Passed { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime FinishedAtUtc { get; set; } = DateTime.UtcNow;
    public IReadOnlyList<SmartaSelfTestEntry> RecentEntries { get; set; } = Array.Empty<SmartaSelfTestEntry>();
}

public class TemperatureHistoryPoint
{
    public DateTime TimestampUtc { get; set; }
    public double TemperatureCelsius { get; set; }
}

public class SpeedSample
{
    public long OffsetBytes { get; set; }
    public int BlockSizeBytes { get; set; }
    public double ThroughputMbps { get; set; }
    public DateTime TimestampUtc { get; set; }
    public int ErrorCount { get; set; }
}

public class TestHistoryItem
{
    public Guid TestId { get; set; }
    public Guid DriveId { get; set; }
    public string DriveName { get; set; } = string.Empty;
    public string DrivePath { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime TestDate { get; set; }
    public string TestType { get; set; } = string.Empty;
    public QualityGrade Grade { get; set; }
    public double Score { get; set; }
    public double AverageSpeed { get; set; }
    public double PeakSpeed { get; set; }
    public double MinSpeed { get; set; }
    public long TotalBytesTested { get; set; }
    public int ErrorCount { get; set; }
    public SmartaData? SmartaData { get; set; }
    public IReadOnlyList<SpeedSample>? SurfaceSamples { get; set; }
}

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int TotalItems { get; set; }
    public int PageSize { get; set; }
    public int PageIndex { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
}

public class CompareItem
{
    public string Label { get; set; } = string.Empty;
    public string Value1 { get; set; } = string.Empty;
    public string Value2 { get; set; } = string.Empty;
}

public class DriveCompareItem
{
    public Guid DriveId { get; set; }
    public string DriveName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int TotalTests { get; set; }
    public DateTime? LastTestDate { get; set; }
}
