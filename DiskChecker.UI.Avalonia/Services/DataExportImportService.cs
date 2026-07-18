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
            ExportScope.SelectedDisks when selectedDiskIds?.Count > 0 =>
                allCards.Where(c => selectedDiskIds.Contains(c.Id)).ToList(),
            _ => allCards
        };

        // Načíst test session a certifikáty pro vybrané disky
        var allSessions = new List<TestSession>();
        var allCertificates = new List<DiskCertificate>();

        foreach (var card in filteredCards)
        {
            ct.ThrowIfCancellationRequested();

            var sessions = await _repository.GetTestSessionsAsync(card.Id);
            allSessions.AddRange(sessions);

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

        // Serializovat do souboru
        var json = JsonSerializer.Serialize(package, _jsonOptions);
        package.Metadata.TotalSizeBytes = System.Text.Encoding.UTF8.GetByteCount(json);

        // Přepsat s korektní velikostí
        var finalPackage = new
        {
            metadata = package.Metadata,
            diskCards = package.DiskCards,
            testSessions = package.TestSessions,
            certificates = package.Certificates
        };

        var finalJson = JsonSerializer.Serialize(finalPackage, _jsonOptions);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(filePath, finalJson, ct);

        return filePath;
    }

    public async Task<ExportMetadata?> PeekMetadataAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("metadata", out var metaEl))
                return null;

            return JsonSerializer.Deserialize<ExportMetadata>(metaEl.GetRawText(), _jsonOptions);
        }
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
            var json = await File.ReadAllTextAsync(filePath, ct);
            var package = JsonSerializer.Deserialize<DataExportPackage>(json, _jsonOptions);

            if (package == null)
            {
                result.Success = false;
                result.ErrorMessage = "Neplatný formát exportního souboru.";
                return result;
            }

            foreach (var importedCard in package.DiskCards)
            {
                ct.ThrowIfCancellationRequested();

                var existingCard = await _repository.GetBySerialNumberAsync(importedCard.SerialNumber);

                if (existingCard != null)
                {
                    if (mode == ImportMode.Replace)
                    {
                        // Nahradit existující kartu - aktualizovat údaje
                        existingCard.ModelName = importedCard.ModelName;
                        existingCard.DevicePath = importedCard.DevicePath;
                        existingCard.DiskType = importedCard.DiskType;
                        existingCard.InterfaceType = importedCard.InterfaceType;
                        existingCard.Capacity = importedCard.Capacity;
                        existingCard.FirmwareVersion = importedCard.FirmwareVersion;
                        existingCard.ConnectionType = importedCard.ConnectionType;
                        existingCard.OverallGrade = importedCard.OverallGrade;
                        existingCard.OverallScore = importedCard.OverallScore;
                        existingCard.Notes = importedCard.Notes;
                        existingCard.PowerOnHours = importedCard.PowerOnHours;
                        existingCard.PowerCycleCount = importedCard.PowerCycleCount;

                        await _repository.UpdateAsync(existingCard);

                        // Importovat test session pro tento disk
                        var cardSessions = package.TestSessions
                            .Where(s => s.DiskCardId == importedCard.Id)
                            .ToList();

                        foreach (var session in cardSessions)
                        {
                            ct.ThrowIfCancellationRequested();
                            session.DiskCardId = existingCard.Id;
                            session.Id = 0; // Nový záznam
                            await _repository.CreateTestSessionAsync(session);
                            result.TestSessionsImported++;
                        }

                        // Importovat certifikáty
                        var cardCerts = package.Certificates
                            .Where(c => c.DiskCardId == importedCard.Id)
                            .ToList();

                        foreach (var cert in cardCerts)
                        {
                            ct.ThrowIfCancellationRequested();
                            cert.DiskCardId = existingCard.Id;
                            cert.Id = 0;
                            await _repository.CreateCertificateAsync(cert);
                            result.CertificatesImported++;
                        }

                        result.DisksImported++;
                    }
                    else // ImportMode.Add
                    {
                        // Pouze doplnit chybějící testy
                        var existingSessions = await _repository.GetTestSessionsAsync(existingCard.Id);
                        var existingSessionIds = new HashSet<Guid>(existingSessions.Select(s => s.SessionId));

                        var cardSessions = package.TestSessions
                            .Where(s => s.DiskCardId == importedCard.Id && !existingSessionIds.Contains(s.SessionId))
                            .ToList();

                        foreach (var session in cardSessions)
                        {
                            ct.ThrowIfCancellationRequested();
                            session.DiskCardId = existingCard.Id;
                            session.Id = 0;
                            await _repository.CreateTestSessionAsync(session);
                            result.TestSessionsImported++;
                        }

                        // Doplnit chybějící certifikáty
                        var existingCerts = await _repository.GetCertificatesAsync(existingCard.Id);
                        var existingCertNumbers = new HashSet<string>(existingCerts.Select(c => c.CertificateNumber));

                        var cardCerts = package.Certificates
                            .Where(c => c.DiskCardId == importedCard.Id && !existingCertNumbers.Contains(c.CertificateNumber))
                            .ToList();

                        foreach (var cert in cardCerts)
                        {
                            ct.ThrowIfCancellationRequested();
                            cert.DiskCardId = existingCard.Id;
                            cert.Id = 0;
                            await _repository.CreateCertificateAsync(cert);
                            result.CertificatesImported++;
                        }

                        if (cardSessions.Count == 0 && cardCerts.Count == 0)
                        {
                            result.DisksSkipped++;
                        }
                        else
                        {
                            result.DisksImported++;
                        }
                    }
                }
                else
                {
                    // Nový disk - vytvořit včetně testů a certifikátů
                    importedCard.Id = 0;
                    var created = await _repository.CreateAsync(importedCard);

                    var cardSessions = package.TestSessions
                        .Where(s => s.DiskCardId == importedCard.Id)
                        .ToList();

                    foreach (var session in cardSessions)
                    {
                        ct.ThrowIfCancellationRequested();
                        session.DiskCardId = created.Id;
                        session.Id = 0;
                        await _repository.CreateTestSessionAsync(session);
                        result.TestSessionsImported++;
                    }

                    var cardCerts = package.Certificates
                        .Where(c => c.DiskCardId == importedCard.Id)
                        .ToList();

                    foreach (var cert in cardCerts)
                    {
                        ct.ThrowIfCancellationRequested();
                        cert.DiskCardId = created.Id;
                        cert.Id = 0;
                        await _repository.CreateCertificateAsync(cert);
                        result.CertificatesImported++;
                    }

                    result.DisksImported++;
                }
            }

            result.Success = true;
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Import byl zrušen.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Warnings.Add($"Chyba při importu: {ex.Message}");
        }

        return result;
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
