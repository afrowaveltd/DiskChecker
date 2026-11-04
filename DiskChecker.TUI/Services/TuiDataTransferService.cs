using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.TUI.Services;

/// <summary>Header of a JSON data export.</summary>
public sealed class TuiExportMetadata
{
    public string Version { get; set; } = "1.0.0";

    public DateTime ExportedAt { get; set; }

    public string ApplicationVersion { get; set; } = "1.0.0";

    public int DiskCount { get; set; }

    public int TestSessionCount { get; set; }

    public int CertificateCount { get; set; }

    public long TotalSizeBytes { get; set; }
}

/// <summary>Envelope of a JSON data export: <c>{metadata, diskCards, testSessions, certificates}</c>.</summary>
public sealed class TuiDataPackage
{
    public TuiExportMetadata Metadata { get; set; } = new();

    public List<DiskCard> DiskCards { get; set; } = new();

    public List<TestSession> TestSessions { get; set; } = new();

    public List<DiskCertificate> Certificates { get; set; } = new();
}

/// <summary>Outcome of a JSON export or import.</summary>
public sealed record TuiDataTransferResult(
    bool Success,
    string Path,
    int Disks,
    int TestSessions,
    int Certificates,
    string Message,
    IReadOnlyList<string> Warnings)
{
    public static TuiDataTransferResult Failure(string path, string message) =>
        new(false, path, 0, 0, 0, message, Array.Empty<string>());
}

/// <summary>
/// Terminal counterpart of the desktop <c>DataExportImportService</c>. Exports every disk
/// card with its test sessions and certificates into one JSON envelope and imports it back
/// with the merge strategy <em>Add</em>: existing serial numbers, session GUIDs and
/// certificate numbers are never duplicated.
/// </summary>
public sealed class TuiDataTransferService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    private readonly IDiskCardRepository _repository;

    public TuiDataTransferService(IDiskCardRepository repository, string? defaultExportDirectory = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        DefaultExportDirectory = defaultExportDirectory ?? DefaultDirectory();
    }

    /// <summary>Directory used when no explicit export path is given.</summary>
    public string DefaultExportDirectory { get; }

    /// <summary>Writes every disk card, test session and certificate into one JSON file.</summary>
    public async Task<TuiDataTransferResult> ExportAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var cards = await _repository.GetAllAsync().ConfigureAwait(false);
        var sessions = new List<TestSession>();
        var certificates = new List<DiskCertificate>();

        foreach (var card in cards)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sessions.AddRange(await _repository.GetTestSessionsAsync(card.Id).ConfigureAwait(false));
            certificates.AddRange(await _repository.GetCertificatesAsync(card.Id).ConfigureAwait(false));
        }

        var package = new TuiDataPackage
        {
            Metadata = new TuiExportMetadata
            {
                Version = "1.0.0",
                ExportedAt = DateTime.UtcNow,
                ApplicationVersion = typeof(TuiDataTransferService).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                DiskCount = cards.Count,
                TestSessionCount = sessions.Count,
                CertificateCount = certificates.Count,
            },
            DiskCards = cards,
            TestSessions = sessions,
            Certificates = certificates,
        };

        var envelope = new
        {
            metadata = package.Metadata,
            diskCards = package.DiskCards,
            testSessions = package.TestSessions,
            certificates = package.Certificates,
        };

        string json = JsonSerializer.Serialize(envelope, JsonOptions);
        package.Metadata.TotalSizeBytes = System.Text.Encoding.UTF8.GetByteCount(json);
        json = JsonSerializer.Serialize(envelope, JsonOptions);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);

        return new TuiDataTransferResult(
            true,
            filePath,
            cards.Count,
            sessions.Count,
            certificates.Count,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Export dokončen: {cards.Count} disků, {sessions.Count} testů, {certificates.Count} certifikátů."),
            Array.Empty<string>());
    }

    /// <summary>Imports a JSON export using the merge strategy <em>Add</em>.</summary>
    public async Task<TuiDataTransferResult> ImportAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            return TuiDataTransferResult.Failure(filePath, "Exportní soubor nebyl nalezen.");
        }

        TuiDataPackage? package;
        try
        {
            string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            package = JsonSerializer.Deserialize<TuiDataPackage>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return TuiDataTransferResult.Failure(filePath, "Neplatný formát exportního souboru: " + ex.Message);
        }

        if (package is null)
        {
            return TuiDataTransferResult.Failure(filePath, "Neplatný formát exportního souboru.");
        }

        int disksImported = 0;
        int sessionsImported = 0;
        int sessionsSkipped = 0;
        int certificatesImported = 0;
        int certificatesSkipped = 0;
        var warnings = new List<string>();

        foreach (var importedCard in package.DiskCards)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Capture the source id before any mutation: the sessions and certificates in
            // the package reference the id the card had when the export was written.
            int sourceCardId = importedCard.Id;
            var cardSessions = package.TestSessions
                .Where(session => session.DiskCardId == sourceCardId)
                .ToList();
            var cardCertificates = package.Certificates
                .Where(certificate => certificate.DiskCardId == sourceCardId)
                .ToList();

            DiskCard? existing = string.IsNullOrWhiteSpace(importedCard.SerialNumber)
                ? null
                : await _repository.GetBySerialNumberAsync(importedCard.SerialNumber).ConfigureAwait(false);

            if (existing is null)
            {
                importedCard.Id = 0;
                DetachCard(importedCard);
                var created = await _repository.CreateAsync(importedCard).ConfigureAwait(false);
                disksImported++;

                var sessionIds = new Dictionary<Guid, int>();
                foreach (var session in cardSessions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    session.Id = 0;
                    session.DiskCardId = created.Id;
                    session.DiskCard = null;
                    var stored = await _repository.CreateTestSessionAsync(session).ConfigureAwait(false);
                    sessionIds[session.SessionId] = stored.Id;
                    sessionsImported++;
                }

                foreach (var certificate in cardCertificates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    certificate.Id = 0;
                    certificate.DiskCardId = created.Id;
                    certificate.DiskCard = null;
                    certificate.TestSession = null;
                    if (certificate.TestSessionId > 0 && sessionIds.Count > 0)
                    {
                        certificate.TestSessionId = 0;
                    }

                    await _repository.CreateCertificateAsync(certificate).ConfigureAwait(false);
                    certificatesImported++;
                }

                continue;
            }

            var existingSessions = await _repository.GetTestSessionsAsync(existing.Id).ConfigureAwait(false);
            var seenSessions = new HashSet<Guid>(existingSessions.Select(session => session.SessionId));
            int addedSessions = 0;
            foreach (var session in cardSessions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seenSessions.Add(session.SessionId))
                {
                    sessionsSkipped++;
                    continue;
                }

                session.Id = 0;
                session.DiskCardId = existing.Id;
                session.DiskCard = null;
                await _repository.CreateTestSessionAsync(session).ConfigureAwait(false);
                sessionsImported++;
                addedSessions++;
            }

            var existingCertificates = await _repository.GetCertificatesAsync(existing.Id).ConfigureAwait(false);
            var seenCertificates = new HashSet<string>(
                existingCertificates.Select(certificate => certificate.CertificateNumber),
                StringComparer.OrdinalIgnoreCase);
            int addedCertificates = 0;
            foreach (var certificate in cardCertificates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string number = certificate.CertificateNumber ?? string.Empty;
                if (!seenCertificates.Add(number))
                {
                    certificatesSkipped++;
                    continue;
                }

                certificate.Id = 0;
                certificate.DiskCardId = existing.Id;
                certificate.DiskCard = null;
                certificate.TestSession = null;
                await _repository.CreateCertificateAsync(certificate).ConfigureAwait(false);
                certificatesImported++;
                addedCertificates++;
            }

            if (addedSessions == 0 && addedCertificates == 0)
            {
                warnings.Add("Disk " + importedCard.SerialNumber + " je již v databázi — přeskočeno.");
            }
            else
            {
                disksImported++;
            }
        }

        return new TuiDataTransferResult(
            true,
            filePath,
            disksImported,
            sessionsImported,
            certificatesImported,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Import dokončen: {disksImported} disků, {sessionsImported} testů, {certificatesImported} certifikátů " +
                $"({sessionsSkipped} testů a {certificatesSkipped} certifikátů přeskočeno)."),
            warnings);
    }

    /// <summary>Reads only the metadata of an export file.</summary>
    public static async Task<TuiExportMetadata?> PeekMetadataAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("metadata", out var metadata))
            {
                return null;
            }

            return JsonSerializer.Deserialize<TuiExportMetadata>(metadata.GetRawText(), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void DetachCard(DiskCard card)
    {
        card.TestSessions = new List<TestSession>();
        card.Certificates = new List<DiskCertificate>();
        card.Volumes = new List<CoreDriveInfo>();
        card.LatestSmartData = null;
    }

    private static string DefaultDirectory()
    {
        string root = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskChecker")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DiskChecker");

        string directory = Path.Combine(root, "Exports");
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only home directory must not break the transfer screen.
        }

        return directory;
    }
}
