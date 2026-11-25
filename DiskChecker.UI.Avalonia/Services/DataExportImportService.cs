using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Implementace služby pro export a import databázových dat.
/// </summary>
public class DataExportImportService : IDataExportImportService
{
    private readonly IDiskCardRepository _repository;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _defaultExportDir;

    public DataExportImportService(IDiskCardRepository repository)
    {
        _repository = repository;

        var appDataDir = GetApplicationDataDirectory();
        _defaultExportDir = Path.Combine(appDataDir, "Exports");
        Directory.CreateDirectory(_defaultExportDir);
    }

    private static string GetApplicationDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiskChecker");
        }
        else if (OperatingSystem.IsLinux())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DiskChecker");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "DiskChecker");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DiskChecker");
    }

    public string GetDefaultExportDirectory() => _defaultExportDir;

    public async Task<string> ExportAsync(string filePath, ExportScope scope, List<int>? selectedDiskIds = null, CancellationToken ct = default)
    {
        _repository.ClearChangeTracker();
        var allCards = await _repository.GetAllAsync();
        var filteredCards = scope switch
        {
            ExportScope.All => allCards,
            ExportScope.MeasurementsAndDisks => allCards,
            ExportScope.SelectedDisks =>
                allCards.Where(c => selectedDiskIds?.Contains(c.Id) == true).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };

        // Keep only one disk's list and one complete summary in memory at a time.
        // JSON object property ordering is not significant; metadata is emitted last
        // so the counts describe the actual records written.
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        int sessionCount = 0;
        int certificateCount = 0;
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();
                writer.WriteStartArray("diskCards");
                foreach (var card in filteredCards)
                {
                    ct.ThrowIfCancellationRequested();
                    JsonSerializer.Serialize(writer, card, _jsonOptions);
                    await writer.FlushAsync(ct);
                }
                writer.WriteEndArray();

                writer.WriteStartArray("testSessions");
                foreach (var card in filteredCards)
                {
                    ct.ThrowIfCancellationRequested();
                    var sessions = await _repository.GetTestSessionsAsync(card.Id);
                    foreach (var session in sessions)
                    {
                        ct.ThrowIfCancellationRequested();
                        var summary = await _repository.GetTestSessionWithoutSamplesAsync(session.Id)
                            ?? throw new InvalidOperationException($"Test {session.Id} disappeared during export.");
                        JsonSerializer.Serialize(writer, summary, _jsonOptions);
                        sessionCount++;
                        await writer.FlushAsync(ct);
                    }
                }
                writer.WriteEndArray();

                writer.WriteStartArray("certificates");
                if (scope != ExportScope.MeasurementsAndDisks)
                {
                    foreach (var card in filteredCards)
                    {
                        ct.ThrowIfCancellationRequested();
                        _repository.ClearChangeTracker();
                        var certificates = await _repository.GetCertificatesAsync(card.Id);
                        foreach (var certificate in certificates)
                        {
                            ct.ThrowIfCancellationRequested();
                            JsonSerializer.Serialize(writer, certificate, _jsonOptions);
                            certificateCount++;
                            await writer.FlushAsync(ct);
                        }
                    }
                }
                writer.WriteEndArray();

                writer.WritePropertyName("metadata");
                JsonSerializer.Serialize(writer, new ExportMetadata
                {
                    Version = "1.0.0",
                    ExportedAt = DateTime.UtcNow,
                    ApplicationVersion = GetAppVersion(),
                    Scope = scope,
                    DiskCount = filteredCards.Count,
                    TestSessionCount = sessionCount,
                    CertificateCount = certificateCount,
                    TotalSizeBytes = 0
                }, _jsonOptions);
                writer.WriteEndObject();
                await writer.FlushAsync(ct);
                await stream.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            _repository.ClearChangeTracker();
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        return filePath;
    }

    public async Task<ExportMetadata?> PeekMetadataAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var metadata = await ReadMetadataAsync(stream, ct);
            if (metadata != null)
                metadata.TotalSizeBytes = stream.Length;
            return metadata;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }

    // Limit memory to the largest individual JSON token (or metadata object),
    // not the size of the entire history. Very large individual tokens are
    // deliberately rejected by preview; the ordinary import remains separate.
    private const int MaxPreviewBufferBytes = 16 * 1024 * 1024;

    private static async Task<ExportMetadata?> ReadMetadataAsync(Stream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[65536];
        int buffered = 0;
        bool first = true;
        bool rootStarted = false;
        bool metadataPending = false;
        ExportMetadata? metadata = null;
        var state = new JsonReaderState();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (buffered == buffer.Length)
            {
                if (buffer.Length >= MaxPreviewBufferBytes)
                    throw new JsonException("A JSON token or metadata object exceeds the preview limit.");
                Array.Resize(ref buffer, Math.Min(buffer.Length * 2, MaxPreviewBufferBytes));
            }

            int read = await stream.ReadAsync(buffer.AsMemory(buffered), ct);
            bool final = read == 0;
            buffered += read;

            if (first)
            {
                if (!final && buffered < 3)
                    continue;
                first = false;
                if (buffered >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                {
                    buffered -= 3;
                    Buffer.BlockCopy(buffer, 3, buffer, 0, buffered);
                }
            }

            int consumed = ScanMetadataChunk(buffer.AsSpan(0, buffered), final,
                ref state, ref rootStarted, ref metadataPending, ref metadata);
            buffered -= consumed;
            if (buffered > 0)
                Buffer.BlockCopy(buffer, consumed, buffer, 0, buffered);

            if (final)
            {
                if (!rootStarted || buffered != 0 || metadataPending)
                    throw new JsonException("Incomplete export package.");
                return metadata;
            }
        }
    }

    private static int ScanMetadataChunk(ReadOnlySpan<byte> bytes, bool final,
        ref JsonReaderState state, ref bool rootStarted, ref bool metadataPending,
        ref ExportMetadata? metadata)
    {
        var reader = new Utf8JsonReader(bytes, final, state);
        while (true)
        {
            var previousState = reader.CurrentState;
            int previousOffset = checked((int)reader.BytesConsumed);
            if (!reader.Read())
                break;

            if (!rootStarted)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException("An export package must be a JSON object.");
                rootStarted = true;
            }

            if (metadataPending)
            {
                // TryParseValue preserves the reader on incomplete data. Rewind
                // to before the value token so the next chunk retries correctly.
                if (!JsonDocument.TryParseValue(ref reader, out var document))
                {
                    state = previousState;
                    return previousOffset;
                }
                using (document)
                {
                    metadata = document!.RootElement.Deserialize<ExportMetadata>(_jsonOptions);
                }
                metadataPending = false;
                continue;
            }

            if (reader.TokenType == JsonTokenType.PropertyName &&
                reader.CurrentDepth == 1 && reader.ValueTextEquals("metadata"u8))
            {
                metadataPending = true;
            }
        }

        state = reader.CurrentState;
        return checked((int)reader.BytesConsumed);
    }

    public async Task<ImportResult> ImportAsync(string filePath, ImportMode mode, CancellationToken ct = default)
    {
        var result = new ImportResult();

        if (!File.Exists(filePath))
        {
            result.Success = false;
            result.ErrorMessage = "Soubor nebyl nalezen.";
            return result;
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            var package = await JsonSerializer.DeserializeAsync<DataExportPackage>(stream, _jsonOptions, ct)
                ?? throw new InvalidDataException("Empty export package.");
            ValidatePackage(package);
            if (mode is not ImportMode.Add and not ImportMode.Replace)
                throw new ArgumentOutOfRangeException(nameof(mode));
            _repository.ClearChangeTracker();

            await _repository.ExecuteHistoricalImportAsync(async () =>
            {
                // Original database IDs are foreign keys inside the transport file only.
                // They must never be interpreted as identifiers in the destination DB.
                var sessionsByCard = package.TestSessions.ToLookup(s => s.DiskCardId);
                var certificatesByCard = package.Certificates.ToLookup(c => c.DiskCardId);
                foreach (var importedCard in package.DiskCards)
                {
                    ct.ThrowIfCancellationRequested();
                    int sourceCardId = importedCard.Id;
                    var cardSessions = sessionsByCard[sourceCardId];
                    var cardCerts = certificatesByCard[sourceCardId];
                    var existingCard = await _repository.GetBySerialNumberAsync(importedCard.SerialNumber);
                    bool changed = existingCard == null;
                    DiskCard destination;
                    if (existingCard == null)
                    {
                        importedCard.Id = 0;
                        // Hardware paths and related objects belong to the source machine.
                        importedCard.DevicePath = string.Empty;
                        importedCard.TestSessions.Clear();
                        importedCard.Certificates.Clear();
                        importedCard.Volumes.Clear();
                        importedCard.TestCount = 0;
                        destination = await _repository.ImportDiskCardAsync(importedCard);
                    }
                    else
                    {
                        destination = existingCard;
                        if (mode == ImportMode.Replace)
                        {
                            // Replace descriptive metadata, never delete historical measurements
                            // or overwrite local hardware paths and protection flags.
                            destination.ModelName = importedCard.ModelName;
                            destination.Manufacturer = importedCard.Manufacturer;
                            destination.ModelFamily = importedCard.ModelFamily;
                            destination.DiskType = importedCard.DiskType;
                            destination.InterfaceType = importedCard.InterfaceType;
                            destination.Capacity = importedCard.Capacity;
                            destination.FirmwareVersion = importedCard.FirmwareVersion;
                            destination.ConnectionType = importedCard.ConnectionType;
                            destination.OverallGrade = importedCard.OverallGrade;
                            destination.OverallScore = importedCard.OverallScore;
                            destination.Notes = importedCard.Notes;
                            destination.PowerOnHours = importedCard.PowerOnHours;
                            destination.PowerCycleCount = importedCard.PowerCycleCount;
                            _repository.ClearChangeTracker();
                            await _repository.UpdateAsync(destination);
                            changed = true;
                        }
                    }

                    var existingSessions = (await _repository.GetTestSessionsAsync(destination.Id))
                        .ToDictionary(s => s.SessionId);
                    var localSessionIds = new Dictionary<int, int>();
                    foreach (var session in cardSessions)
                    {
                        ct.ThrowIfCancellationRequested();
                        int sourceSessionId = session.Id;
                        if (existingSessions.TryGetValue(session.SessionId, out var existing))
                        {
                            localSessionIds.Add(sourceSessionId, existing.Id);
                            result.TestSessionsSkipped++;
                            continue;
                        }

                        session.Id = 0;
                        session.DiskCardId = destination.Id;
                        session.DiskCard = null;
                        session.CertificateId = null;
                        session.ChartImagePath = null;
                        var saved = await _repository.ImportTestSessionAsync(session);
                        localSessionIds.Add(sourceSessionId, saved.Id);
                        existingSessions.Add(saved.SessionId, saved);
                        result.TestSessionsImported++;
                        changed = true;
                    }

                    var existingCerts = (await _repository.GetCertificatesAsync(destination.Id))
                        .ToDictionary(c => c.CertificateNumber, StringComparer.Ordinal);
                    foreach (var cert in cardCerts)
                    {
                        ct.ThrowIfCancellationRequested();
                        int sessionId = localSessionIds[cert.TestSessionId];
                        if (existingCerts.TryGetValue(cert.CertificateNumber, out var existing))
                        {
                            if (existing.TestSessionId != sessionId)
                                throw new InvalidDataException("Certificate number belongs to a different local test.");
                            result.CertificatesSkipped++;
                            continue;
                        }

                        cert.Id = 0;
                        cert.DiskCardId = destination.Id;
                        cert.TestSessionId = sessionId;
                        cert.DiskCard = null;
                        cert.TestSession = null;
                        cert.PairedCertificate = null;
                        // A JSON transfer does not carry the source machine's PDF files.
                        cert.PdfGenerated = false;
                        cert.PdfPath = null;
                        cert.ChartImagePath = null;
                        foreach (var attribute in cert.SmartAttributes)
                            attribute.Id = 0;
                        string originalNumber = cert.CertificateNumber;
                        var saved = await _repository.CreateCertificateAsync(cert);
                        // The ordinary certificate creation flow may allocate a new number
                        // on a global collision. Historical identity must never be changed.
                        if (!string.Equals(saved.CertificateNumber, originalNumber, StringComparison.Ordinal))
                            throw new InvalidDataException("Certificate number belongs to a different local certificate.");
                        existingCerts.Add(saved.CertificateNumber, saved);
                        result.CertificatesImported++;
                        changed = true;
                    }

                    if (changed) result.DisksImported++;
                    else result.DisksSkipped++;
                }
            }, ct);

            result.Success = true;
        }
        catch (OperationCanceledException)
        {
            result = new ImportResult { Success = false, ErrorMessage = "Import byl zrušen." };
        }
        catch (Exception ex)
        {
            result = new ImportResult { Success = false, ErrorMessage = ex.Message };
            result.Warnings.Add($"Chyba při importu: {ex.Message}");
        }

        return result;
    }

    private static void ValidatePackage(DataExportPackage package)
    {
        if (package.Metadata == null || package.Metadata.Version != "1.0.0" ||
            package.DiskCards == null || package.TestSessions == null || package.Certificates == null)
            throw new InvalidDataException("Unsupported or incomplete export package.");

        var cards = new Dictionary<int, DiskCard>();
        var serials = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in package.DiskCards)
        {
            if (card.Id <= 0 || string.IsNullOrWhiteSpace(card.SerialNumber) ||
                !cards.TryAdd(card.Id, card) || !serials.Add(card.SerialNumber))
                throw new InvalidDataException("Invalid or duplicate disk identity in export.");
        }

        var sessions = new Dictionary<int, TestSession>();
        var sessionGuids = new HashSet<Guid>();
        foreach (var session in package.TestSessions)
        {
            if (session.Id <= 0 || !cards.ContainsKey(session.DiskCardId) ||
                session.SessionId == Guid.Empty || session.StartedAt == default ||
                !sessions.TryAdd(session.Id, session) || !sessionGuids.Add(session.SessionId))
                throw new InvalidDataException("Invalid or duplicate test identity in export.");
        }

        var numbers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cert in package.Certificates)
        {
            if (string.IsNullOrWhiteSpace(cert.CertificateNumber) || !numbers.Add(cert.CertificateNumber) ||
                !sessions.TryGetValue(cert.TestSessionId, out var session) ||
                session.DiskCardId != cert.DiskCardId)
                throw new InvalidDataException("Certificate references a missing or unrelated test.");
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }
}
