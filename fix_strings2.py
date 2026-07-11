import os
import re

viewmodels_dir = r'K:\Afrowave Projects\DiskChecker\DiskChecker.UI.Avalonia\ViewModels'

def fix_file(filename, replacements):
    """Apply list of (old, new) replacements to a file."""
    filepath = os.path.join(viewmodels_dir, filename)
    if not os.path.exists(filepath):
        print(f"SKIP: {filename} not found")
        return
    
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    for old, new in replacements:
        if old in content:
            content = content.replace(old, new)
            print(f"  Replaced in {filename}")
        else:
            print(f"  NOT FOUND in {filename}: {old[:60]}...")
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

# ============================================================
# BackupViewModel.cs
# ============================================================
fix_file('BackupViewModel.cs', [
    # ShowConfirmationAsync with "💾 Spustit zálohování"
    ('''ShowConfirmationAsync(
            "💾 Spustit zálohování",
            $"Záloha bude provedena na vybrané cílové disky.\\n\\n" +
            $"Zdrojový disk: {SelectedDrive?.Name ?? "Neznámý"}\\n" +
            $"Cílové disky: {string.Join(", ", TargetDrives.Where(t => t.IsSelected).Select(t => t.DisplayName))}\\n" +
            $"Režim: {SelectedBackupMode}\\n" +
            $"Data: {TotalBytesToBackupText}\\n\\n" +
            $"OPRAVDU SPUSTIT ZÁLOHOVÁNÍ?");''',
     '''ShowConfirmationAsync(
            L.Get("Common.Confirmation"),
            $"Záloha bude provedena na vybrané cílové disky.\\n\\n" +
            $"Zdrojový disk: {SelectedDrive?.Name ?? "Neznámý"}\\n" +
            $"Cílové disky: {string.Join(", ", TargetDrives.Where(t => t.IsSelected).Select(t => t.DisplayName))}\\n" +
            $"Režim: {SelectedBackupMode}\\n" +
            $"Data: {TotalBytesToBackupText}\\n\\n" +
            $"OPRAVDU SPUSTIT ZÁLOHOVÁNÍ?");'''),
])

# ============================================================
# FullReportViewerViewModel.cs
# ============================================================
fix_file('FullReportViewerViewModel.cs', [
    # ShowInfoAsync with "Tisk"
    ('''ShowInfoAsync(
                "Tisk",
                "Report byl otevřen ve výchozí aplikaci. Pro bezpečný tisk použijte tisk přímo v otevřeném okně (Ctrl+P). Automatický shell tisk byl vypnut kvůli přetížení systému.");''',
     '''ShowInfoAsync(
                L.Get("Common.Print"),
                "Report byl otevřen ve výchozí aplikaci. Pro bezpečný tisk použijte tisk přímo v otevřeném okně (Ctrl+P). Automatický shell tisk byl vypnut kvůli přetížení systému.");'''),
])

# ============================================================
# HistoryViewModel.cs
# ============================================================
fix_file('HistoryViewModel.cs', [
    # ShowConfirmationAsync with "Vymazat historii"
    ('''ShowConfirmationAsync(
                "Vymazat historii",
                "Opravdu chcete vymazat celou historii testů?");''',
     '''ShowConfirmationAsync(
                L.Get("Common.Confirmation"),
                "Opravdu chcete vymazat celou historii testů?");'''),
    
    # ShowConfirmationAsync with "Potvrzení"
    ('''ShowConfirmationAsync(
                    "Potvrzení", 
                    $"Opravdu chcete smazat test '{SelectedTest.TestType}' z {SelectedTest.TestDate:dd.MM.yyyy HH:mm}?");''',
     '''ShowConfirmationAsync(
                    L.Get("Common.Confirmation"), 
                    $"Opravdu chcete smazat test '{SelectedTest.TestType}' z {SelectedTest.TestDate:dd.MM.yyyy HH:mm}?");'''),
])

# ============================================================
# ReportViewModel.cs
# ============================================================
fix_file('ReportViewModel.cs', [
    # ShowConfirmationAsync with "Potvrzení" (delete report)
    ('''ShowConfirmationAsync(
                    "Potvrzení", 
                    $"Opravdu chcete smazat test \\"{SelectedReport.Title}\\" z {SelectedReport.TestDate:dd.MM.yyyy HH:mm}?");''',
     '''ShowConfirmationAsync(
                    L.Get("Common.Confirmation"), 
                    $"Opravdu chcete smazat test \\"{SelectedReport.Title}\\" z {SelectedReport.TestDate:dd.MM.yyyy HH:mm}?");'''),
    
    # ShowConfirmationAsync with "Certifikát vytvořen"
    ('''ShowConfirmationAsync(
                    "Certifikát vytvořen",
                    $"Certifikát byl uložen do PDF:\\n{result.PdfPath}\\n\\nOtevřít soubor?");''',
     '''ShowConfirmationAsync(
                    L.Get("Common.Confirmation"),
                    $"Certifikát byl uložen do PDF:\\n{result.PdfPath}\\n\\nOtevřít soubor?");'''),
    
    # ShowConfirmationAsync with "Plný report připraven"
    ('''ShowConfirmationAsync(
                    "Plný report připraven",
                    (reducedMode ? "Report byl vytvořen v omezeném režimu kvůli nedostatku místa.\\n\\n" : string.Empty) +
                    $"Report byl uložen do:\\n{filePath}\\n\\n" +
                    (graphImagePath != null ? $"Graf (PNG):\\n{graphImagePath}\\n\\n" : string.Empty) +
                    "Otevřít report v aplikaci?");''',
     '''ShowConfirmationAsync(
                    L.Get("Common.Confirmation"),
                    (reducedMode ? "Report byl vytvořen v omezeném režimu kvůli nedostatku místa.\\n\\n" : string.Empty) +
                    $"Report byl uložen do:\\n{filePath}\\n\\n" +
                    (graphImagePath != null ? $"Graf (PNG):\\n{graphImagePath}\\n\\n" : string.Empty) +
                    "Otevřít report v aplikaci?");'''),
])

# ============================================================
# RestoreViewModel.cs
# ============================================================
fix_file('RestoreViewModel.cs', [
    # ShowConfirmationAsync with "⚠️ VAROVÁNÍ - Původní disk"
    ('''ShowConfirmationAsync(
                "⚠️ VAROVÁNÍ – Původní disk",
                $"Vybraný cílový disk je PŮVODNÍ disk, ze kterého byla záloha vytvořena!\\n\\n" +
                $"Obnova na tento disk PŘEPÍŠE všechna data na něm.\\n\\n" +
                $"OPRAVDU chcete pokračovat?");''',
     '''ShowConfirmationAsync(
                L.Get("Common.Warning"),
                $"Vybraný cílový disk je PŮVODNÍ disk, ze kterého byla záloha vytvořena!\\n\\n" +
                $"Obnova na tento disk PŘEPÍŠE všechna data na něm.\\n\\n" +
                $"OPRAVDU chcete pokračovat?");'''),
    
    # ShowConfirmationAsync with "💾 Spustit obnovu"
    ('''ShowConfirmationAsync(
            "💾 Spustit obnovu",
            $"Obnova bude provedena na disk: {targetDisk.DisplayName}\\n\\n" +
            $"Zdrojová záloha: {SelectedBackup.SourceModel}\\n" +
            $"Datum zálohy: {SelectedBackup.BackupDate}\\n" +
            $"Režim: {SelectedBackup.Mode}\\n" +
            $"Data: {SelectedBackup.TotalBytesText}\\n\\n" +
            $"OPRAVDU SPUSTIT OBNOVU?");''',
     '''ShowConfirmationAsync(
            L.Get("Common.Confirmation"),
            $"Obnova bude provedena na disk: {targetDisk.DisplayName}\\n\\n" +
            $"Zdrojová záloha: {SelectedBackup.SourceModel}\\n" +
            $"Datum zálohy: {SelectedBackup.BackupDate}\\n" +
            $"Režim: {SelectedBackup.Mode}\\n" +
            $"Data: {SelectedBackup.TotalBytesText}\\n\\n" +
            $"OPRAVDU SPUSTIT OBNOVU?");'''),
])

# ============================================================
# SeekTestViewModel.cs
# ============================================================
fix_file('SeekTestViewModel.cs', [
    # ShowConfirmationAsync with "Vynucený seek test na selhávajícím disku"
    ('''ShowConfirmationAsync(
                "Vynucený seek test na selhávajícím disku",
                "SMART/rekomendační modul označil disk jako příliš opotřebený nebo selhávající a výchozí doporučený počet seeků je 0.\\n\\n" +
                $"Požadujete vynucené spuštění {SelectedSeekCount} seeků proti doporučení. Test může disk dále zatížit nebo urychlit selhání.\\n\\n" +
                "Pokračovat pouze z diagnostických důvodů?");''',
     '''ShowConfirmationAsync(
                L.Get("Common.Warning"),
                "SMART/rekomendační modul označil disk jako příliš opotřebený nebo selhávající a výchozí doporučený počet seeků je 0.\\n\\n" +
                $"Požadujete vynucené spuštění {SelectedSeekCount} seeků proti doporučení. Test může disk dále zatížit nebo urychlit selhání.\\n\\n" +
                "Pokračovat pouze z diagnostických důvodů?");'''),
    
    # ShowConfirmationAsync with "Překročení bezpečného limitu"
    ('''ShowConfirmationAsync(
                "Překročení bezpečného limitu",
                $"Požadovaný počet seeků ({SelectedSeekCount}) překračuje bezpečný limit ({MaxSafeSeekCount}) " +
                $"doporučený na základě SMART dat.\\n\\nChcete přesto pokračovat?");''',
     '''ShowConfirmationAsync(
                L.Get("Common.Warning"),
                $"Požadovaný počet seeků ({SelectedSeekCount}) překračuje bezpečný limit ({MaxSafeSeekCount}) " +
                $"doporučený na základě SMART dat.\\n\\nChcete přesto pokračovat?");'''),
])

# ============================================================
# SettingsViewModel.cs
# ============================================================
fix_file('SettingsViewModel.cs', [
    # ShowConfirmationAsync with "Potvrzení" (reset)
    ('''ShowConfirmationAsync(
                    "Potvrzení", 
                    "Opravdu chcete resetovat všechna nastavení na výchozí hodnoty?");''',
     '''ShowConfirmationAsync(
                    L.Get("Common.Confirmation"), 
                    "Opravdu chcete resetovat všechna nastavení na výchozí hodnoty?");'''),
    
    # ShowConfirmationAsync with "Obnovení ze zálohy"
    ('''ShowConfirmationAsync(
                    "Obnovení ze zálohy",
                    $"Opravdu chcete obnovit data ze zálohy?\\n\\n" +
                    $"Záloha: {SelectedBackup.FileName}\\n" +
                    $"Datum: {SelectedBackup.CreatedAt:dd.MM.yyyy HH:mm}\\n\\n" +
                    $"VAROVÁNÍ: Aktuální data budou přepsána!");''',
     '''ShowConfirmationAsync(
                    L.Get("Common.Confirmation"),
                    $"Opravdu chcete obnovit data ze zálohy?\\n\\n" +
                    $"Záloha: {SelectedBackup.FileName}\\n" +
                    $"Datum: {SelectedBackup.CreatedAt:dd.MM.yyyy HH:mm}\\n\\n" +
                    $"VAROVÁNÍ: Aktuální data budou přepsána!");'''),
])

# ============================================================
# SmartCheckViewModel.cs
# ============================================================
fix_file('SmartCheckViewModel.cs', [
    # ShowConfirmationAsync with "Sledovat průběh?"
    ('''ShowConfirmationAsync(
            "Sledovat průběh?",
            "Chcete sledovat průběh self-testu v reálném čase?\\n\\n" +
            "Aplikace bude pravidelně dotazovat SMART data pro zobrazení pokroku.");''',
     '''ShowConfirmationAsync(
            L.Get("Common.Confirmation"),
            "Chcete sledovat průběh self-testu v reálném čase?\\n\\n" +
            "Aplikace bude pravidelně dotazovat SMART data pro zobrazení pokroku.");'''),
])

# ============================================================
# SurfaceTestViewModel.cs
# ============================================================
fix_file('SurfaceTestViewModel.cs', [
    # ShowDangerConfirmationAsync with "Potvrzení sanitizace"
    ('''ShowDangerConfirmationAsync(
             "Potvrzení sanitizace",
             $"Vybraný profil \\"{profile.Name}\\" přepíše obsah disku {SelectedDrive?.Name ?? "Neznámý"}.\\n\\n" +
             $"Tato operace je NEVRATNÁ a všechna data budou ztracena.\\n\\n" +
             $"OPRAVDU CHCETE POKRAČOVAT?");''',
     '''ShowDangerConfirmationAsync(
             L.Get("Common.Confirmation"),
             $"Vybraný profil \\"{profile.Name}\\" přepíše obsah disku {SelectedDrive?.Name ?? "Neznámý"}.\\n\\n" +
             $"Tato operace je NEVRATNÁ a všechna data budou ztracena.\\n\\n" +
             $"OPRAVDU CHCETE POKRAČOVAT?");'''),
])

print("\nDone with phase 2!")
