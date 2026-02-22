using System.Globalization;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;

namespace DiskChecker.Infrastructure.Persistence;

public static class LocalizationService
{
    private static readonly Dictionary<string, Dictionary<string, string>> _translations = new()
    {
        ["cs-CZ"] = new Dictionary<string, string>
        {
            ["app_name"] = "DiskChecker",
            ["main_menu"] = "Hlavní menu",
            ["check_disk"] = "Kontrola disku (SMART)",
            ["full_test"] = "Úplný test (zápis/nula + kontrola)",
            ["history"] = "Historie testů",
            ["compare"] = "Porovnání disků",
            ["settings"] = "Nastavení (jazyk)",
            ["exit"] = "Konec",
            ["select_language"] = "Vyberte jazyk / Select Language",
            ["language_set"] = "Jazyk nastaven na: {0}",
            ["scanning_drives"] = "Získávám seznam disků...",
            ["loading_history"] = "Načítám historii...",
            ["scan_complete"] = "Nalezeno {0} disků",
            ["drive_selection"] = "Výběr disku",
            ["select_drive"] = "Vyberte disk pro test",
            ["quality_grade"] = "Kvalita disku",
            ["score"] = "Skóre",
            ["summary"] = "Shrnutí",
            ["warnings"] = "UPOZORNĚNÍ",
            ["no_warnings"] = "Žádná upozornění",
            ["test_duration"] = "Doba testu",
            ["bytes_written"] = "Zapsáno bytů",
            ["errors"] = "Chyb"
        },
        ["en-US"] = new Dictionary<string, string>
        {
            ["app_name"] = "DiskChecker",
            ["main_menu"] = "Main Menu",
            ["check_disk"] = "Check Disk (SMART)",
            ["full_test"] = "Full Test (write/zero + verify)",
            ["history"] = "Test History",
            ["compare"] = "Compare Disks",
            ["settings"] = "Settings (Language)",
            ["exit"] = "Exit",
            ["select_language"] = "Select Language / Vyberte jazyk",
            ["language_set"] = "Language set to: {0}",
            ["scanning_drives"] = "Scanning drives...",
            ["loading_history"] = "Loading history...",
            ["scan_complete"] = "Found {0} drives",
            ["drive_selection"] = "Drive Selection",
            ["select_drive"] = "Select disk for test",
            ["quality_grade"] = "Disk Quality",
            ["score"] = "Score",
            ["summary"] = "Summary",
            ["warnings"] = "WARNINGS",
            ["no_warnings"] = "No warnings",
            ["test_duration"] = "Test Duration",
            ["bytes_written"] = "Bytes Written",
            ["errors"] = "Errors"
        }
    };

    public static string Get(this Dictionary<string, string> dict, string key, string lang = "cs-CZ")
    {
        if (dict.TryGetValue(key, out var value))
        {
            return value;
        }
        return key;
    }
}
