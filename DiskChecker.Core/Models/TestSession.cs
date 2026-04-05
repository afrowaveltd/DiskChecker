using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace DiskChecker.Core.Models;

/// <summary>
/// Represents a single test session with all metrics and results.
/// </summary>
public class TestSession
{
    private static readonly JsonSerializerOptions SmartJsonOptions = new()
    {
        WriteIndented = false
    };

    private SmartaData? _smartBefore;
    private SmartaData? _smartAfter;
    private string? _smartBeforeJson;
    private string? _smartAfterJson;

    public int Id { get; set; }
    
    /// <summary>
    /// Foreign key to DiskCard
    /// </summary>
    public int DiskCardId { get; set; }
    
    /// <summary>
    /// Navigation property
    /// </summary>
    public DiskCard? DiskCard { get; set; }
    
    /// <summary>
    /// Unique session identifier
    /// </summary>
    public Guid SessionId { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Type of test performed
    /// </summary>
    public TestType TestType { get; set; }
    
    /// <summary>
    /// When the test started
    /// </summary>
    public DateTime StartedAt { get; set; }
    
    /// <summary>
    /// When the test completed
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>
    /// Test duration (calculated, but stored for EF)
    /// </summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>
    /// SMART data before test
    /// </summary>
    [NotMapped]
    public SmartaData? SmartBefore
    {
        get
        {
            if (_smartBefore == null && !string.IsNullOrWhiteSpace(_smartBeforeJson))
            {
                _smartBefore = DeserializeSmartSnapshot(_smartBeforeJson);
            }

            return _smartBefore;
        }
        set
        {
            _smartBefore = value;
            _smartBeforeJson = SerializeSmartSnapshot(value);
        }
    }

    /// <summary>
    /// SMART data after test
    /// </summary>
    [NotMapped]
    public SmartaData? SmartAfter
    {
        get
        {
            if (_smartAfter == null && !string.IsNullOrWhiteSpace(_smartAfterJson))
            {
                _smartAfter = DeserializeSmartSnapshot(_smartAfterJson);
            }

            return _smartAfter;
        }
        set
        {
            _smartAfter = value;
            _smartAfterJson = SerializeSmartSnapshot(value);
        }
    }

    /// <summary>
    /// Persisted SMART snapshot before test (JSON).
    /// </summary>
    public string? SmartBeforeJson
    {
        get => _smartBeforeJson;
        set
        {
            _smartBeforeJson = value;
            _smartBefore = null;
        }
    }

    /// <summary>
    /// Persisted SMART snapshot after test (JSON).
    /// </summary>
    public string? SmartAfterJson
    {
        get => _smartAfterJson;
        set
        {
            _smartAfterJson = value;
            _smartAfter = null;
        }
    }
    
    /// <summary>
    /// SMART attribute changes during test
    /// </summary>
    public List<SmartAttributeChange> SmartChanges { get; set; } = new();
    
    /// <summary>
    /// Test status
    /// </summary>
    public TestStatus Status { get; set; }
    
    /// <summary>
    /// Whether this was a destructive test
    /// </summary>
    public bool IsDestructive { get; set; }
    
    /// <summary>
    /// Whether disk was locked during test
    /// </summary>
    public bool WasLocked { get; set; }
    
    // ========== Write Phase Metrics ==========
    
    /// <summary>
    /// Total bytes written
    /// </summary>
    public long BytesWritten { get; set; }
    
    /// <summary>
    /// Average write speed in MB/s
    /// </summary>
    public double AverageWriteSpeedMBps { get; set; }
    
    /// <summary>
    /// Maximum write speed in MB/s
    /// </summary>
    public double MaxWriteSpeedMBps { get; set; }
    
    /// <summary>
    /// Minimum write speed in MB/s
    /// </summary>
    public double MinWriteSpeedMBps { get; set; }
    
    /// <summary>
    /// Write speed standard deviation
    /// </summary>
    public double WriteSpeedStdDev { get; set; }
    
    /// <summary>
    /// Time taken for write phase
    /// </summary>
    public TimeSpan WriteDuration { get; set; }
    
    /// <summary>
    /// Write errors count
    /// </summary>
    public int WriteErrors { get; set; }
    
    // ========== Read/Verify Phase Metrics ==========
    
    /// <summary>
    /// Total bytes read
    /// </summary>
    public long BytesRead { get; set; }
    
    /// <summary>
    /// Average read speed in MB/s
    /// </summary>
    public double AverageReadSpeedMBps { get; set; }
    
    /// <summary>
    /// Maximum read speed in MB/s
    /// </summary>
    public double MaxReadSpeedMBps { get; set; }
    
    /// <summary>
    /// Minimum read speed in MB/s
    /// </summary>
    public double MinReadSpeedMBps { get; set; }
    
    /// <summary>
    /// Read speed standard deviation
    /// </summary>
    public double ReadSpeedStdDev { get; set; }
    
    /// <summary>
    /// Time taken for read phase
    /// </summary>
    public TimeSpan ReadDuration { get; set; }
    
    /// <summary>
    /// Read errors count
    /// </summary>
    public int ReadErrors { get; set; }
    
    /// <summary>
    /// Verification errors (non-zero bytes found)
    /// </summary>
    public int VerificationErrors { get; set; }
    
    // ========== Temperature Metrics ==========
    
    /// <summary>
    /// Starting temperature in Celsius
    /// </summary>
    public int? StartTemperature { get; set; }
    
    /// <summary>
    /// Maximum temperature during test in Celsius
    /// </summary>
    public int? MaxTemperature { get; set; }
    
    /// <summary>
    /// Average temperature during test in Celsius
    /// </summary>
    public double? AverageTemperature { get; set; }
    
    /// <summary>
    /// Temperature samples taken during test
    /// </summary>
    public List<TemperatureSample> TemperatureSamples { get; set; } = new();
    
    // ========== Speed Samples ==========
    
    /// <summary>
    /// Speed samples taken during write phase
    /// </summary>
    public List<SpeedSample> WriteSamples { get; set; } = new();
    
    /// <summary>
    /// Speed samples taken during read phase
    /// </summary>
    public List<SpeedSample> ReadSamples { get; set; } = new();
    
    // ========== Partition and Format ==========
    
    /// <summary>
    /// Whether partition was created
    /// </summary>
    public bool PartitionCreated { get; set; }
    
    /// <summary>
    /// Partition scheme (GPT, MBR)
    /// </summary>
    public string? PartitionScheme { get; set; }
    
    /// <summary>
    /// Whether disk was formatted
    /// </summary>
    public bool WasFormatted { get; set; }
    
    /// <summary>
    /// File system used (NTFS, exFAT, etc.)
    /// </summary>
    public string? FileSystem { get; set; }
    
    /// <summary>
    /// Volume label assigned
    /// </summary>
    public string? VolumeLabel { get; set; }
    
    /// <summary>
    /// Overall test result
    /// </summary>
    public TestResult Result { get; set; }
    
    /// <summary>
    /// Grade assigned (A-F)
    /// </summary>
    public string Grade { get; set; } = "?";
    
    /// <summary>
    /// Score (0-100)
    /// </summary>
    public double Score { get; set; }
    
    /// <summary>
    /// Health assessment
    /// </summary>
    public HealthAssessment HealthAssessment { get; set; } = HealthAssessment.Unknown;
    
    /// <summary>
    /// Absolute path to a cached chart image generated for this test session.
    /// </summary>
    public string? ChartImagePath { get; set; }

    /// <summary>
    /// Certificate ID if one was generated
    /// </summary>
    public int? CertificateId { get; set; }
    
    /// <summary>
    /// Any errors or warnings
    /// </summary>
    public List<TestError> Errors { get; set; } = new();
    
    /// <summary>
    /// User notes for this test session
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Gets compact diagnostic flags derived from session notes.
    /// </summary>
    [NotMapped]
    public string DiagnosticFlagsText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Notes))
            {
                return "OK";
            }

            var flags = new List<string>();
            if (Notes.Contains("kritické", StringComparison.OrdinalIgnoreCase) || Notes.Contains("kritický", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("KRIT");
            }

            if (Notes.Contains("propad", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("DROP");
            }

            if (Notes.Contains("nestabil", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("CV");
            }

            if (Notes.Contains("histor", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("HIST");
            }

            if (Notes.Contains("SMART", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("SMART");
            }

            return flags.Count == 0 ? "OK" : string.Join(" | ", flags);
        }
    }

    /// <summary>
    /// Gets a concise diagnostic badge caption derived from session notes.
    /// </summary>
    [NotMapped]
    public string DiagnosticBadgeText =>
        HasCriticalDiagnosticSignals ? "KRITICKÉ SIGNÁLY" :
        HasWarningDiagnosticSignals ? "VAROVNÉ SIGNÁLY" :
        "STABILNÍ PRŮBĚH";

    /// <summary>
    /// Gets a background color for the diagnostic badge.
    /// </summary>
    [NotMapped]
    public string DiagnosticBadgeBackground =>
        HasCriticalDiagnosticSignals ? "#FDECEC" :
        HasWarningDiagnosticSignals ? "#FFF4DB" :
        "#EAF7EE";

    /// <summary>
    /// Gets a foreground color for the diagnostic badge.
    /// </summary>
    [NotMapped]
    public string DiagnosticBadgeForeground =>
        HasCriticalDiagnosticSignals ? "#B42318" :
        HasWarningDiagnosticSignals ? "#B54708" :
        "#027A48";

    /// <summary>
    /// Gets whether critical diagnostic signals were detected.
    /// </summary>
    [NotMapped]
    public bool HasCriticalDiagnosticSignals =>
        !string.IsNullOrWhiteSpace(Notes) &&
        (Notes.Contains("kritické", StringComparison.OrdinalIgnoreCase) || Notes.Contains("kritický", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets whether warning-level diagnostic signals were detected.
    /// </summary>
    [NotMapped]
    public bool HasWarningDiagnosticSignals =>
        !string.IsNullOrWhiteSpace(Notes) &&
        (Notes.Contains("propad", StringComparison.OrdinalIgnoreCase) ||
         Notes.Contains("nestabil", StringComparison.OrdinalIgnoreCase) ||
         Notes.Contains("histor", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Alias for UI bindings expecting test timestamp.
    /// </summary>
    public DateTime TestedAt => StartedAt;

    /// <summary>
    /// Alias for UI bindings expecting tested sectors count.
    /// </summary>
    public long SectorsTested => BytesRead > 0 ? BytesRead / 512 : 0;

    /// <summary>
    /// Alias for UI bindings expecting aggregate error count.
    /// </summary>
    public int ErrorCount => Errors.Count + WriteErrors + ReadErrors + VerificationErrors;

    /// <summary>
    /// Alias for UI bindings expecting current/average temperature.
    /// </summary>
    public int Temperature => (int)Math.Round(AverageTemperature ?? MaxTemperature ?? StartTemperature ?? 0);

    /// <summary>
    /// Indicates whether test data suggests thermal throttling.
    /// </summary>
    [NotMapped]
    public bool IsThermalThrottlingSuspected
    {
        get
        {
            var baselineTemp = StartTemperature ?? AverageTemperature ?? MaxTemperature;
            var peakTemp = MaxTemperature ?? AverageTemperature ?? StartTemperature;
            if (!baselineTemp.HasValue || !peakTemp.HasValue)
            {
                return false;
            }

            var tempRise = peakTemp.Value - baselineTemp.Value;
            var writeDrop = MaxWriteSpeedMBps > 0
                ? (1d - (MinWriteSpeedMBps / MaxWriteSpeedMBps)) * 100d
                : 0d;

            return tempRise >= 8 && writeDrop >= 20;
        }
    }

    /// <summary>
    /// User-friendly throttling status for tabular UI.
    /// </summary>
    [NotMapped]
    public string ThermalThrottlingText => IsThermalThrottlingSuspected ? "⚠ Podezření" : "OK";

    private static string? SerializeSmartSnapshot(SmartaData? smartData)
    {
        if (smartData == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Serialize(smartData, SmartJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static SmartaData? DeserializeSmartSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SmartaData>(json, SmartJsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Type of disk test
/// </summary>
public enum TestType
{
    QuickRead,
    QuickWrite,
    FullRead,
    FullWrite,
    FullReadWrite,
    SurfaceScan,
    Sanitization,
    SmartShort,
    SmartExtended,
    SmartConveyance,
    Custom
}

/// <summary>
/// Test session status
/// </summary>
public enum TestStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    Paused
}

/// <summary>
/// Test result
/// </summary>
public enum TestResult
{
    Pass,
    Fail,
    Warning,
    Inconclusive,
    NotApplicable
}

/// <summary>
/// Health assessment
/// </summary>
public enum HealthAssessment
{
    Excellent,
    Good,
    Fair,
    Poor,
    Critical,
    Unknown
}

/// <summary>
/// Temperature sample during test
/// </summary>
public class TemperatureSample
{
    public DateTime Timestamp { get; set; }
    public int TemperatureCelsius { get; set; }
    public string Phase { get; set; } = string.Empty; // "Write", "Read", "Verify"
    public double ProgressPercent { get; set; }
}

/// <summary>
/// SMART attribute change during test
/// </summary>
public class SmartAttributeChange
{
    public int AttributeId { get; set; }
    public string AttributeName { get; set; } = string.Empty;
    public long ValueBefore { get; set; }
    public long ValueAfter { get; set; }
    public long Change { get; set; }
    public string? Warning { get; set; }
}

/// <summary>
/// Test error or warning
/// </summary>
public class TestError
{
    public DateTime Timestamp { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public bool IsCritical { get; set; }
    public string? Details { get; set; }
}
