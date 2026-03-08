using System;
using System.Collections.Generic;

namespace DiskChecker.Core.Models;

// Alias for backward compatibility
public class SmartData : SmartCheckResult { }

public enum QualityGrade { Unknown, A, B, C, D, E, F }

public enum QualityRating
{
    APlus, A, B, C, D, E, F
}

public static class QualityRatingExtensions
{
    public static QualityGrade GetGrade(QualityRating rating)
    {
        return rating switch
        {
            QualityRating.APlus => QualityGrade.A,
            QualityRating.A => QualityGrade.B,
            QualityRating.B => QualityGrade.C,
            QualityRating.C => QualityGrade.D,
            QualityRating.D => QualityGrade.E,
            QualityRating.E => QualityGrade.F,
            QualityRating.F => QualityGrade.F,
            _ => QualityGrade.Unknown
        };
    }
    
    public static double GetScore(QualityRating rating)
    {
        return rating switch
        {
            QualityRating.APlus => 98,
            QualityRating.A => 90,
            QualityRating.B => 80,
            QualityRating.C => 70,
            QualityRating.D => 60,
            QualityRating.E => 50,
            QualityRating.F => 30,
            _ => 0
        };
    }
    
    public static int GetWarnings(QualityRating rating)
    {
        return rating switch
        {
            QualityRating.APlus => 0,
            QualityRating.A => 0,
            QualityRating.B => 1,
            QualityRating.C => 2,
            QualityRating.D => 3,
            QualityRating.E => 4,
            QualityRating.F => 5,
            _ => 0
        };
    }
}

public enum SmartaSelfTestType
{
    Quick,
    ShortTest,
    Extended,
    Conveyance,
    Selective,
    Offline,
    Abort
}

public enum SmartaMaintenanceAction
{
    EnableSmart,
    DisableSmart,
    RunSelfTest,
    ClearPendingSectors,
    Rescan,
    EnableAutoSave,
    DisableAutoSave,
    RunOfflineDataCollection,
    AbortSelfTest
}

public class SmartaAttributeItem
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public byte Value { get; set; }
    public byte Worst { get; set; }
    public uint RawValue { get; set; }
    public int Threshold { get; set; }
    public bool IsOk { get; set; }
    public byte Current { get; set; }
    public string? WhenFailed { get; set; }
}

public enum SmartaSelfTestStatus
{
    None,
    Passed,
    Aborted,
    Interrupted,
    Fatal,
    CompletedUnknownFailure,
    ElectricalFailure,
    ServoFailure,
    ReadFailure,
    HandlingDamage,
    Unknown,
    Running
}

public class SmartctlSelfTestStatus
{
    public bool IsRunning { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public int RemainingPercent { get; set; }
    public DateTime? CheckedAtUtc { get; set; }
}

public class SmartaSelfTestEntry
{
    public int Number { get; set; }
    public SmartaSelfTestType Type { get; set; }
    public SmartaSelfTestStatus Status { get; set; }
    public int RemainingPercent { get; set; }
    public DateTime? Timestamp { get; set; }
    public string? Note { get; set; }
    public int LifeTimeHours { get; set; }
    public ulong LbaOfFirstError { get; set; }
}

public class SmartCheckResult
{
    public bool IsEnabled { get; set; }
    public bool IsHealthy { get; set; }
    public bool TestPassed { get; set; }
    public int PowerOnHours { get; set; }
    public long ReallocatedSectorCount { get; set; }
    public long PendingSectorCount { get; set; }
    public long UncorrectableErrorCount { get; set; }
    public double Temperature { get; set; }
    public int? WearLevelingCount { get; set; }
    public List<SmartaAttributeItem> Attributes { get; set; } = new();
    public List<SmartaSelfTestEntry> SelfTests { get; set; } = new();
    public SmartctlSelfTestStatus? CurrentSelfTest { get; set; }
}

public class SmartaData
{
    public string? DeviceModel { get; set; }
    public string? ModelFamily { get; set; }
    public string? SerialNumber { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? Capacity { get; set; }
    public bool IsRotational { get; set; }
    public bool SmartEnabled { get; set; }
    public bool SmartHealthy { get; set; }
    public int PowerOnHours { get; set; }
    public long ReallocatedSectorCount { get; set; }
    public long PendingSectorCount { get; set; }
    public long UncorrectableErrorCount { get; set; }
    public double Temperature { get; set; }
    public int? WearLevelingCount { get; set; }
    public List<SmartaAttributeItem> Attributes { get; set; } = new();
    public List<SmartaSelfTestEntry> SelfTests { get; set; } = new();
    public string? Vendor { get; set; }
    public string? Product { get; set; }
    public string? Revision { get; set; }
    public string? Compliance { get; set; }
    public string? UserCapacity { get; set; }
    public string? SectorSizes { get; set; }
    public string? FormFactor { get; set; }
    public string? RotationRate { get; set; }
    public string? LogicalModelId { get; set; }
    public string? PhysicalModelId { get; set; }
    public string? TestDate { get; set; }
}

public class CoreDriveInfo
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public string? VolumeInfo { get; set; }
    public string? FileSystem { get; set; }
    public bool IsSystemDisk { get; set; }


// Email settings record
public class EmailSettingsRecord
{
    public Guid Id { get; set; }
    public string? SmtpServer { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? SenderEmail { get; set; }
    public string? SenderName { get; set; }
    public string? RecipientEmail { get; set; }
    public bool IsEnabled { get; set; } = true;
}

// Paged result
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

// Test record
public class TestRecord
{
    public Guid Id { get; set; }
    public string? SerialNumber { get; set; }
    public string? Model { get; set; }
    public DateTime TestDate { get; set; }
    public string TestType { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public int Score { get; set; }
    public bool Passed { get; set; }
    public string? Details { get; set; }
}

// SmartaSelfTestReport
public class SmartaSelfTestReport
{
    public DateTime? TestStartTime { get; set; }
    public DateTime? TestEndTime { get; set; }
    public SmartaSelfTestType TestType { get; set; }
    public SmartaSelfTestStatus Status { get; set; }
    public int RemainingPercent { get; set; }
    public string? Message { get; set; }
}

// Temperature history point
public class TemperatureHistoryPoint
{
    public DateTime Timestamp { get; set; }
    public double Temperature { get; set; }
}

// Speed sample
public class SpeedSample
{
    public DateTime Timestamp { get; set; }
    public double ReadSpeed { get; set; }
    public double WriteSpeed { get; set; }
}

// Test history item
public class TestHistoryItem
{
    public Guid Id { get; set; }
    public DateTime TestDate { get; set; }
    public string TestType { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Grade { get; set; } = string.Empty;
    public string? CertificatePath { get; set; }
}

// Compare item
public class CompareItem
{
    public string Label { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string? PreviousValue { get; set; }
    public string? Change { get; set; }
}

// Drive compare item
public class DriveCompareItem
{
    public string SerialNumber { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public List<CompareItem> Items { get; set; } = new();
}
}
