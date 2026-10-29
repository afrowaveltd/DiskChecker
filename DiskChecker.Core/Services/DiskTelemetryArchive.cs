using DiskChecker.Core.Models;

namespace DiskChecker.Core.Services;

/// <summary>
/// Append-only, disk-backed high-resolution throughput log. Twenty fixed-size
/// records are written per block. The block headers form a small on-disk index:
/// a reader skips blocks outside the requested progress range without loading
/// the full test into RAM. Files are named after the persisted session GUID.
/// </summary>
public sealed class DiskTelemetryArchive : IDisposable
{
    private const int Magic = 0x31544344;       // DCT1
    private const int BlockMagic = 0x314B4C42;  // BLK1
    private const int Version = 1;
    private const int BlockCapacity = 20;
    private const int RecordBytes = 42; // ticks, elapsed, percent, bytes, speed, stall, phase
    private const int BlockHeaderBytes = 25;

    private readonly object _gate = new();
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly string _temporaryPath;
    private readonly List<SpeedSample> _pending = new(BlockCapacity);
    private byte _pendingPhase;
    private bool _disposed;

    public static string ArchiveDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DiskChecker", "Telemetry");

    public static string GetSessionPath(Guid sessionGuid) =>
        Path.Combine(ArchiveDirectory, sessionGuid.ToString("N") + ".dat");

    public static bool Exists(Guid sessionGuid) => File.Exists(GetSessionPath(sessionGuid));

    public DiskTelemetryArchive()
    {
        Directory.CreateDirectory(ArchiveDirectory);
        _temporaryPath = Path.Combine(ArchiveDirectory, "pending_" + Guid.NewGuid().ToString("N") + ".dat");
        _stream = new FileStream(_temporaryPath, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read, 32 * 1024);
        _writer = new BinaryWriter(_stream);
        _writer.Write(Magic);
        _writer.Write(Version);
        _writer.Write(BlockCapacity);
        _writer.Write(RecordBytes);
    }

    public void Append(SpeedSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var phase = EncodePhase(sample.Phase);
            if (_pending.Count > 0 && (phase != _pendingPhase || _pending.Count == BlockCapacity))
                FlushBlock();

            _pendingPhase = phase;
            _pending.Add(sample);
            if (_pending.Count == BlockCapacity)
                FlushBlock();
        }
    }

    public void Complete(Guid sessionGuid)
    {
        if (sessionGuid == Guid.Empty)
            throw new ArgumentException("A persisted session GUID is required.", nameof(sessionGuid));
        Dispose();
        File.Move(_temporaryPath, GetSessionPath(sessionGuid), overwrite: false);
    }

    private void FlushBlock()
    {
        if (_pending.Count == 0)
            return;

        _writer.Write(BlockMagic);
        _writer.Write(_pendingPhase);
        _writer.Write(_pending.Count);
        _writer.Write(_pending.Min(s => s.ProgressPercent));
        _writer.Write(_pending.Max(s => s.ProgressPercent));
        foreach (var sample in _pending)
        {
            _writer.Write(sample.Timestamp.ToBinary());
            _writer.Write(sample.Elapsed?.TotalMilliseconds ?? double.NaN);
            _writer.Write(sample.ProgressPercent);
            _writer.Write(sample.BytesProcessed);
            _writer.Write(sample.SpeedMBps);
            _writer.Write(sample.IsStalled);
            _writer.Write(_pendingPhase);
        }
        _pending.Clear();
        _writer.Flush();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            FlushBlock();
            _writer.Dispose();
            _stream.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Query only requested progress blocks. A small extremum-aware buffer
    /// caps returned data while preserving sharp dips and their neighbors.
    /// </summary>
    public static List<TestTelemetrySample> Read(
        Guid sessionGuid, double startPercent = 0, double endPercent = 100,
        int bucketsPerPhase = 1000)
    {
        var path = GetSessionPath(sessionGuid);
        if (!File.Exists(path))
            return new List<TestTelemetrySample>();

        var start = Math.Clamp(Math.Min(startPercent, endPercent), 0d, 100d);
        var end = Math.Clamp(Math.Max(startPercent, endPercent), 0d, 100d);
        // Use the entire bucket budget inside the selected zoom window,
        // rather than allocating only 2% of it when zooming from 25–27%.
        var windowEnd = end > start ? end : Math.Min(100d, start + 1e-9);
        var windowStart = windowEnd > start ? start : Math.Max(0d, end - 1e-9);
        var write = new ProgressSpeedSampleBuffer(bucketsPerPhase, windowStart, windowEnd);
        var read = new ProgressSpeedSampleBuffer(bucketsPerPhase, windowStart, windowEnd);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 32 * 1024);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 16 || reader.ReadInt32() != Magic ||
            reader.ReadInt32() != Version || reader.ReadInt32() != BlockCapacity ||
            reader.ReadInt32() != RecordBytes)
            throw new InvalidDataException("Unknown or damaged DiskChecker telemetry archive.");

        while (stream.Position + BlockHeaderBytes <= stream.Length)
        {
            var marker = reader.ReadInt32();
            if (marker != BlockMagic)
                break;
            var phase = reader.ReadByte();
            var count = reader.ReadInt32();
            var blockMin = reader.ReadDouble();
            var blockMax = reader.ReadDouble();
            if (count < 1 || count > BlockCapacity || stream.Position + count * RecordBytes > stream.Length)
                break; // Interrupted write: ignore the incomplete trailing block.

            if (blockMax < start || blockMin > end)
            {
                stream.Seek(count * RecordBytes, SeekOrigin.Current);
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                var timestamp = DateTime.FromBinary(reader.ReadInt64());
                var elapsed = reader.ReadDouble();
                var percent = reader.ReadDouble();
                var bytes = reader.ReadInt64();
                var speed = reader.ReadDouble();
                var stalled = reader.ReadBoolean();
                var recordPhase = reader.ReadByte();
                if (percent < start || percent > end)
                    continue;

                var sample = new SpeedSample
                {
                    Timestamp = timestamp,
                    Elapsed = double.IsFinite(elapsed) ? TimeSpan.FromMilliseconds(elapsed) : null,
                    ProgressPercent = percent,
                    BytesProcessed = bytes,
                    SpeedMBps = speed,
                    IsStalled = stalled,
                    Phase = recordPhase == 1 ? "Write" : "Read"
                };
                (recordPhase == 1 ? write : read).Add(sample);
            }
        }

        var result = new List<TestTelemetrySample>(write.Count + read.Count);
        AppendPhase(write, TelemetrySamplePhase.Write, result);
        AppendPhase(read, TelemetrySamplePhase.Read, result);
        return result;
    }

    private static void AppendPhase(ProgressSpeedSampleBuffer source,
        TelemetrySamplePhase phase, List<TestTelemetrySample> output)
    {
        for (var i = 0; i < source.Count; i++)
        {
            var s = source.Samples[i];
            output.Add(new TestTelemetrySample
            {
                Phase = phase,
                SequenceIndex = i,
                TimestampUtc = s.Timestamp,
                ElapsedMs = s.Elapsed?.TotalMilliseconds,
                ProgressPercent = s.ProgressPercent,
                BytesProcessed = s.BytesProcessed,
                SpeedMBps = s.SpeedMBps,
                IsStalled = s.IsStalled
            });
        }
    }

    private static byte EncodePhase(string? phase) =>
        string.Equals(phase, "Read", StringComparison.OrdinalIgnoreCase) ? (byte)2 : (byte)1;
}
