using System.Text.Json;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Exact lifecycle state of a long-running disk operation. A 100% progress
/// update alone is NEVER sufficient evidence that verification succeeded.
/// </summary>
public enum LongTestRecoveryState
{
    Running,
    Completed,
    Failed,
    Cancelled,
    DatabaseSaved,
    ArtifactsSaved
}

/// <summary>
/// Small, versioned, self-contained safety checkpoint. The existing binary
/// DiskTelemetryArchive remains authoritative for high-resolution charts.
/// </summary>
public sealed class LongTestRecoverySnapshot
{
    public int FormatVersion { get; set; } = 1;
    public Guid RunId { get; set; }
    public string TestKind { get; set; } = string.Empty;
    public string DevicePath { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string? FirmwareVersion { get; set; }
    public long DeviceCapacityBytes { get; set; }
    public CoreBusType BusType { get; set; }
    public bool IsRemovable { get; set; }
    public bool? IsSolidState { get; set; }
    public bool? IsRotational { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public LongTestRecoveryState State { get; set; }
    public string Phase { get; set; } = "Starting";
    public long BytesProcessed { get; set; }
    public long PhaseTotalBytes { get; set; }
    public double PhasePercent { get; set; }
    public int ErrorCount { get; set; }
    public string? Failure { get; set; }
    public SanitizationResult? FinalSanitizationResult { get; set; }
    public SmartaData? SmartBefore { get; set; }
    public SmartaData? SmartAfter { get; set; }
    public string? BorrowedSmartProvenance { get; set; }
    public List<SpeedSample>? WriteSamples { get; set; }
    public List<SpeedSample>? ReadSamples { get; set; }
    public Guid? DatabaseSessionGuid { get; set; }
}

/// <summary>Non-destructive recovery inventory item, including damaged files.</summary>
public sealed record LongTestRecoveryEntry(
    string FilePath, LongTestRecoverySnapshot? Snapshot, string? ValidationError);

/// <summary>
/// Crash-surviving checkpoint for multi-hour disk tests. It never writes to the
/// raw disk being tested. Each replacement is fully written and flushed in the
/// same folder BEFORE the prior checkpoint is replaced. Failed replacements
/// leave the previous complete checkpoint intact and never delete recovery data.
/// A record does not authorize any automatic raw-device resume.
/// </summary>
public sealed class LongTestRecoveryJournal
{
    private const int MaxSamplesPerPhase = 5_000;
    private const int MaximumSnapshotBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private readonly object _gate = new();
    private readonly LongTestRecoverySnapshot _snapshot;
    private readonly string _filePath;
    private DateTime _lastWrittenUtc;
    private double _lastWrittenPercent;
    private string _lastWrittenPhase = string.Empty;

    /// <summary>Current user's default recovery folder, separate from final chart archives.</summary>
    public static string RecoveryDirectory =>
        Path.Combine(DiskTelemetryArchive.ArchiveDirectory, "Recovery");

    /// <summary>The stable identity of this single physical test attempt.</summary>
    public Guid RunId => _snapshot.RunId;

    /// <summary>Full durable snapshot path; never delete it manually during a failed save.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Begin a physical test. Failure to create the first durable checkpoint
    /// propagates, allowing the caller to abort BEFORE opening a raw device.
    /// </summary>
    public LongTestRecoveryJournal(
        CoreDriveInfo drive, string testKind = "Sanitization", string? directoryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(drive);
        if (string.IsNullOrWhiteSpace(drive.Path) || drive.TotalSize <= 0)
            throw new ArgumentException("A physical drive path and positive capacity are required.", nameof(drive));
        if (string.IsNullOrWhiteSpace(testKind))
            throw new ArgumentException("A test kind is required.", nameof(testKind));

        var directory = directoryOverride ?? RecoveryDirectory;
        Directory.CreateDirectory(directory);
        var now = DateTime.UtcNow;
        _snapshot = new LongTestRecoverySnapshot
        {
            RunId = Guid.NewGuid(),
            TestKind = testKind,
            DevicePath = drive.Path,
            SerialNumber = drive.SerialNumber,
            ModelName = drive.Model ?? drive.Name,
            FirmwareVersion = drive.FirmwareVersion,
            DeviceCapacityBytes = drive.TotalSize,
            BusType = drive.BusType,
            IsRemovable = drive.IsRemovable,
            IsSolidState = drive.IsSolidState,
            IsRotational = drive.IsRotational,
            StartedAtUtc = now,
            UpdatedAtUtc = now,
            State = LongTestRecoveryState.Running
        };
        _filePath = Path.Combine(directory, "run_" + _snapshot.RunId.ToString("N") + ".json");
        SaveLocked();
    }

    /// <summary>
    /// Persist monotonic progress on phase change, each 5%, or every ten
    /// seconds. Cached intermediate updates are not a disk-resume boundary.
    /// </summary>
    public void Checkpoint(string phase, long processedBytes, long totalBytes, double percent, int errors = 0)
    {
        if (string.IsNullOrWhiteSpace(phase)) throw new ArgumentException("Phase required.", nameof(phase));
        lock (_gate)
        {
            if (_snapshot.State != LongTestRecoveryState.Running) return;
            var now = DateTime.UtcNow;
            var boundedPercent = double.IsFinite(percent) ? Math.Clamp(percent, 0d, 100d) : 0d;
            if (string.Equals(phase, _snapshot.Phase, StringComparison.Ordinal) &&
                processedBytes < _snapshot.BytesProcessed)
                return; // Ignore stale callbacks from the same phase.
            _snapshot.Phase = phase;
            _snapshot.BytesProcessed = Math.Max(0, processedBytes);
            _snapshot.PhaseTotalBytes = Math.Max(0, totalBytes);
            _snapshot.PhasePercent = boundedPercent;
            _snapshot.ErrorCount = Math.Max(_snapshot.ErrorCount, errors);
            if (phase == _lastWrittenPhase &&
                boundedPercent - _lastWrittenPercent < 5d &&
                now - _lastWrittenUtc < TimeSpan.FromSeconds(10))
                return;
            _snapshot.UpdatedAtUtc = now;
            SaveLocked();
        }
    }

    /// <summary>
    /// Record the actual sanitizer outcome before ANY database writes.
    /// Full result, SMART snapshots and bounded graph samples permit later
    /// idempotent import. Cancellation/failure is explicit, never inferred
    /// from a 100%-complete graph.
    /// </summary>
    public void FinishSanitization(
        SanitizationResult result,
        IEnumerable<SpeedSample>? writeSamples,
        IEnumerable<SpeedSample>? readSamples,
        SmartaData? smartBefore,
        SmartaData? smartAfter,
        string? borrowedSmartProvenance = null,
        bool userCancelled = false)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            if (_snapshot.State != LongTestRecoveryState.Running)
                throw new InvalidOperationException("A test can be finalized only once.");
            _snapshot.FinalSanitizationResult = result;
            _snapshot.WriteSamples = SnapshotSamples(writeSamples);
            _snapshot.ReadSamples = SnapshotSamples(readSamples);
            _snapshot.SmartBefore = smartBefore;
            _snapshot.SmartAfter = smartAfter;
            _snapshot.BorrowedSmartProvenance = borrowedSmartProvenance;
            _snapshot.EndedAtUtc = DateTime.UtcNow;
            _snapshot.State = userCancelled
                ? LongTestRecoveryState.Cancelled
                : result.Success ? LongTestRecoveryState.Completed : LongTestRecoveryState.Failed;
            _snapshot.Failure = result.Success ? null : result.ErrorMessage;
            _snapshot.UpdatedAtUtc = DateTime.UtcNow;
            SaveLocked();
        }
    }

    /// <summary>Explicit terminal failure before a SanitizationResult was produced.</summary>
    public void RecordFailure(string reason, bool cancelled = false)
    {
        lock (_gate)
        {
            if (_snapshot.State != LongTestRecoveryState.Running) return;
            _snapshot.State = cancelled ? LongTestRecoveryState.Cancelled : LongTestRecoveryState.Failed;
            _snapshot.Failure = reason;
            _snapshot.EndedAtUtc = DateTime.UtcNow;
            _snapshot.UpdatedAtUtc = DateTime.UtcNow;
            SaveLocked();
        }
    }

    /// <summary>
    /// Record that the authoritative TestSession commit returned successfully.
    /// The recovery file is retained until optional artifacts are also saved.
    /// </summary>
    public void MarkDatabaseSaved(Guid sessionGuid)
    {
        if (sessionGuid == Guid.Empty || sessionGuid != RunId)
            throw new ArgumentException("The persisted session must match this recovery run.", nameof(sessionGuid));
        lock (_gate)
        {
            if (_snapshot.State is not (LongTestRecoveryState.Completed or LongTestRecoveryState.Failed or LongTestRecoveryState.Cancelled))
                throw new InvalidOperationException("Final outcome must be written before marking the session durable.");
            _snapshot.DatabaseSessionGuid = sessionGuid;
            _snapshot.State = LongTestRecoveryState.DatabaseSaved;
            _snapshot.UpdatedAtUtc = DateTime.UtcNow;
            SaveLocked();
        }
    }

    /// <summary>Certificate/PDF persist step was confirmed by the caller.</summary>
    public void MarkArtifactsSaved()
    {
        lock (_gate)
        {
            if (_snapshot.State != LongTestRecoveryState.DatabaseSaved)
                throw new InvalidOperationException("Database must be saved first.");
            _snapshot.State = LongTestRecoveryState.ArtifactsSaved;
            _snapshot.UpdatedAtUtc = DateTime.UtcNow;
            SaveLocked();
        }
    }

    /// <summary>
    /// Delete only after the caller re-queries independent durable DB/certificate
    /// evidence. Returning false keeps the checkpoint intact.
    /// </summary>
    public bool DeleteAfterVerification(Func<Guid, bool> confirmDurable)
    {
        ArgumentNullException.ThrowIfNull(confirmDurable);
        lock (_gate)
        {
            if (_snapshot.State != LongTestRecoveryState.ArtifactsSaved ||
                _snapshot.DatabaseSessionGuid != RunId ||
                !confirmDurable(RunId))
                return false;
            File.Delete(_filePath);
            return true;
        }
    }

    /// <summary>
    /// Read the recovery inventory without modifying, claiming or deleting
    /// live records. Damaged records are returned for explicit inspection.
    /// </summary>
    public static IReadOnlyList<LongTestRecoveryEntry> Inspect(
        string? directoryOverride = null, int maxFiles = 1_000)
    {
        if (maxFiles <= 0) throw new ArgumentOutOfRangeException(nameof(maxFiles));
        var directory = directoryOverride ?? RecoveryDirectory;
        if (!Directory.Exists(directory)) return Array.Empty<LongTestRecoveryEntry>();
        var items = new List<LongTestRecoveryEntry>();
        foreach (var path in Directory.EnumerateFiles(directory, "run_*.json").Take(maxFiles))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaximumSnapshotBytes)
                    throw new InvalidDataException("Recovery snapshot exceeds its format limit.");
                var snapshot = JsonSerializer.Deserialize<LongTestRecoverySnapshot>(
                    File.ReadAllBytes(path), JsonOptions);
                if (snapshot == null || snapshot.FormatVersion != 1 ||
                    snapshot.RunId == Guid.Empty ||
                    Path.GetFileName(path) != "run_" + snapshot.RunId.ToString("N") + ".json" ||
                    string.IsNullOrWhiteSpace(snapshot.TestKind) ||
                    string.IsNullOrWhiteSpace(snapshot.DevicePath) ||
                    snapshot.DeviceCapacityBytes <= 0)
                    throw new InvalidDataException("Recovery snapshot header is missing or invalid.");
                if (snapshot.State is LongTestRecoveryState.Completed or LongTestRecoveryState.DatabaseSaved or LongTestRecoveryState.ArtifactsSaved &&
                    snapshot.FinalSanitizationResult == null && snapshot.TestKind == "Sanitization")
                    throw new InvalidDataException("Completed sanitization lacks the durable outcome.");
                items.Add(new LongTestRecoveryEntry(path, snapshot, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                JsonException or InvalidDataException or NotSupportedException)
            {
                items.Add(new LongTestRecoveryEntry(path, null, ex.Message));
            }
        }
        return items;
    }

    private static List<SpeedSample>? SnapshotSamples(IEnumerable<SpeedSample>? samples)
    {
        if (samples == null) return null;
        var list = samples.Take(MaxSamplesPerPhase + 1).ToList();
        if (list.Count > MaxSamplesPerPhase)
            throw new InvalidDataException("Recovery graph sample budget exceeded.");
        return list;
    }

    private void SaveLocked()
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(_snapshot, JsonOptions);
        if (data.Length > MaximumSnapshotBytes)
            throw new InvalidDataException("Recovery checkpoint exceeds its 16-MiB safety limit.");

        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    File.SetUnixFileMode(temporaryPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _filePath, overwrite: true);
            _lastWrittenUtc = _snapshot.UpdatedAtUtc;
            _lastWrittenPercent = _snapshot.PhasePercent;
            _lastWrittenPhase = _snapshot.Phase;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
