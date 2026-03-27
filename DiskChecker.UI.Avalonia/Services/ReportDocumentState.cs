using System;
using System.IO;

namespace DiskChecker.UI.Avalonia.Services;

/// <summary>
/// Sdílený stav pro práci s naposledy vygenerovaným plným reportem.
/// </summary>
public sealed class ReportDocumentState
{
    /// <summary>
    /// Absolutní cesta k naposledy vygenerovanému reportu.
    /// </summary>
    public string? LastReportPath { get; set; }

    /// <summary>
    /// Určuje, zda existuje dostupný report soubor pro zobrazení.
    /// </summary>
    public bool HasReport
        => !string.IsNullOrWhiteSpace(LastReportPath) && File.Exists(LastReportPath);

    /// <summary>
    /// Vyčistí uložený stav reportu.
    /// </summary>
    public void Clear()
    {
        LastReportPath = null;
    }
}
