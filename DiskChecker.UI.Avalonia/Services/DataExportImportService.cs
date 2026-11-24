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
        var allCards = await _repository.GetAllAsync();
        var filteredCards = scope switch
        {
            ExportScope.All => allCards,
            ExportScope.MeasurementsAndDisks => allCards,
            ExportScope.SelectedDisks =>
                allCards.Where(c => selectedDiskIds?.Contains(c.Id) == true).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };

        // Načíst test session a certifikáty pro vybrané disky
        var allSessions = new List<TestSession>();
        var allCertificates = new List<DiskCertificate>();

        foreach (var card in filteredCards)
        {
            ct.ThrowIfCancellationRequested();

            var sessions = await _repository.GetTestSessionsAsync(card.Id);
            // Export complete scalar/JSON diagnostic summaries, without raw owned sample tables.
            foreach (var session in sessions)
            {
                ct.ThrowIfCancellationRequested();
                allSessions.Add(await _repository.GetTestSessionWithoutSamplesAsync(session.Id)
                    ?? throw new InvalidOperationException($"Test {session.Id} disappeared during export."));
            }

            var certs = await _repository.GetCertificatesAsync(card.Id);
            allCertificates.AddRange(certs);
        }

        // Pro MeasurementsAndDisks vynecháme certifikáty
        if (scope == ExportScope.MeasurementsAndDisks)
        {
            allCertificates.Clear();
        }

        var package = new DataExportPackage
        {
            Metadata = new ExportMetadata
            {
                Version = "1.0.0",
                ExportedAt = DateTime.UtcNow,
                ApplicationVersion = GetAppVersion(),
                Scope = scope,
                DiskCount = filteredCards.Count,
                TestSessionCount = allSessions.Count,
                CertificateCount = allCertificates.Count,
                TotalSizeBytes = 0 // dopočítáme po serializaci
            },
            DiskCards = filteredCards,
            TestSessions = allSessions,
            Certificates = allCertificates
        };

        // Stream the JSON to a sibling temporary file. Never leave a partially
        // written export at the selected destination or overwrite it on cancellation.
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, package, _jsonOptions, ct);
                await stream.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
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
            await using var stream = File.OpenRead(filePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            if (!root.TryGetProperty("metadata", out var metaEl))
                return null;

            var metadata = metaEl.Deserialize<ExportMetadata>(_jsonOptions);
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

            // Original database IDs are foreign keys inside the transport file only.
            // They must never be interpreted as identifiers in the destination DB.
            foreach (var importedCard in package.DiskCards)
            {
                ct.ThrowIfCancellationRequested();
                int sourceCardId = importedCard.Id;
                var cardSessions = package.TestSessions.Where(s => s.DiskCardId == sourceCardId).ToList();
                var cardCerts = package.Certificates.Where(c => c.DiskCardId == sourceCardId).ToList();
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
                    destination = await _repository.CreateAsync(importedCard);
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
                    var saved = await _repository.CreateCertificateAsync(cert);
                    existingCerts.Add(saved.CertificateNumber, saved);
                    result.CertificatesImported++;
                    changed = true;
                }

                if (changed) result.DisksImported++;
                else result.DisksSkipped++;
            }

            result.Success = true;
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Import byl zrušen. Již uložené záznamy mohou zůstat v databázi.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Warnings.Add($"Chyba při importu: {ex.Message}. Již uložené záznamy mohou zůstat v databázi.");
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
