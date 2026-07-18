using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.Services.Interfaces;

/// <summary>
/// Volby pro export dat.
/// </summary>
public enum ExportScope
{
    /// <summary>Exportovat celou databázi (všechny disky, testy, certifikáty).</summary>
    All,
    /// <summary>Exportovat všechna měření a disky (bez certifikátů).</summary>
    MeasurementsAndDisks,
    /// <summary>Exportovat pouze vybrané disky (podle ID).</summary>
    SelectedDisks
}

/// <summary>
/// Volby pro import dat - jak naložit s existujícími záznamy.
/// </summary>
public enum ImportMode
{
    /// <summary>Nahradit existující záznamy (podle sériového čísla).</summary>
    Replace,
    /// <summary>Přidat nová data, existující záznamy ponechat (pouze doplnit chybějící testy).</summary>
    Add
}

/// <summary>
/// Metadata o exportovaném souboru.
/// </summary>
public class ExportMetadata
{
    public string Version { get; set; } = "1.0.0";
    public DateTime ExportedAt { get; set; }
    public string ApplicationVersion { get; set; } = "1.0.0";
    public ExportScope Scope { get; set; }
    public int DiskCount { get; set; }
    public int TestSessionCount { get; set; }
    public int CertificateCount { get; set; }
    public long TotalSizeBytes { get; set; }
}

/// <summary>
/// Kompletní exportní balíček databázových dat.
/// </summary>
public class DataExportPackage
{
    public ExportMetadata Metadata { get; set; } = new();
    public List<DiskCard> DiskCards { get; set; } = new();
    public List<TestSession> TestSessions { get; set; } = new();
    public List<DiskCertificate> Certificates { get; set; } = new();
}

/// <summary>
/// Výsledek importu.
/// </summary>
public class ImportResult
{
    public bool Success { get; set; }
    public int DisksImported { get; set; }
    public int DisksSkipped { get; set; }
    public int TestSessionsImported { get; set; }
    public int TestSessionsSkipped { get; set; }
    public int CertificatesImported { get; set; }
    public int CertificatesSkipped { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Služba pro export a import databázových dat (disky, měření, certifikáty).
/// </summary>
public interface IDataExportImportService
{
    /// <summary>
    /// Exportuje data do JSON souboru.
    /// </summary>
    /// <param name="filePath">Cílový soubor.</param>
    /// <param name="scope">Rozsah exportu.</param>
    /// <param name="selectedDiskIds">ID vybraných disků (pouze pro SelectedDisks).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Cestu k vytvořenému souboru.</returns>
    Task<string> ExportAsync(string filePath, ExportScope scope, List<int>? selectedDiskIds = null, CancellationToken ct = default);

    /// <summary>
    /// Importuje data z JSON souboru.
    /// </summary>
    /// <param name="filePath">Zdrojový soubor.</param>
    /// <param name="mode">Režim importu (Replace/Add).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Výsledek importu.</returns>
    Task<ImportResult> ImportAsync(string filePath, ImportMode mode, CancellationToken ct = default);

    /// <summary>
    /// Získá metadata z exportního souboru bez jeho importu.
    /// </summary>
    Task<ExportMetadata?> PeekMetadataAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Získá výchozí adresář pro exporty.
    /// </summary>
    string GetDefaultExportDirectory();
}
