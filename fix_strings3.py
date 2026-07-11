import os
import re

viewmodels_dir = r'K:\Afrowave Projects\DiskChecker\DiskChecker.UI.Avalonia\ViewModels'

def replace_in_file(filename, old, new):
    """Replace old with new in file, handling UTF-8 properly."""
    filepath = os.path.join(viewmodels_dir, filename)
    if not os.path.exists(filepath):
        print(f"SKIP: {filename} not found")
        return False
    
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    if old in content:
        content = content.replace(old, new)
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"  OK: {filename}")
        return True
    else:
        print(f"  NOT FOUND in {filename}")
        return False

# ============================================================
# BackupViewModel.cs - ShowConfirmationAsync with "💾 Spustit zálohování"
# ============================================================
old = '''        var confirmed = await _dialogService.ShowConfirmationAsync(
            "💾 Spustit zálohování",
            $"Záloha bude provedena na vybrané cílové disky.\\n\\n" +
            $"Zdrojový disk: {SelectedDrive?.Name ?? "Neznámý"}\\n" +
            $"Cílové disky: {string.Join(", ", TargetDrives.Where(t => t.IsSelected).Select(t => t.DisplayName))}\\n" +
            $"Režim: {SelectedBackupMode}\\n" +
            $"Data: {TotalBytesToBackupText}\\n\\n" +
            $"OPRAVDU SPUSTIT ZÁLOHOVÁNÍ?");'''

new = '''        var confirmed = await _dialogService.ShowConfirmationAsync(
            L.Get("Common.Confirmation"),
            $"Záloha bude provedena na vybrané cílové disky.\\n\\n" +
            $"Zdrojový disk: {SelectedDrive?.Name ?? "Neznámý"}\\n" +
            $"Cílové disky: {string.Join(", ", TargetDrives.Where(t => t.IsSelected).Select(t => t.DisplayName))}\\n" +
            $"Režim: {SelectedBackupMode}\\n" +
            $"Data: {TotalBytesToBackupText}\\n\\n" +
            $"OPRAVDU SPUSTIT ZÁLOHOVÁNÍ?");'''

replace_in_file('BackupViewModel.cs', old, new)

# ============================================================
# FullReportViewerViewModel.cs - ShowInfoAsync with "Tisk"
# ============================================================
old = '''            await _dialogService.ShowInfoAsync(
                "Tisk",
                "Report byl otevřen ve výchozí aplikaci. Pro bezpečný tisk použijte tisk přímo v otevřeném okně (Ctrl+P). Automatický shell tisk byl vypnut kvůli přetížení systému.");'''

new = '''            await _dialogService.ShowInfoAsync(
                L.Get("Common.Print"),
                "Report byl otevřen ve výchozí aplikaci. Pro bezpečný tisk použijte tisk přímo v otevřeném okně (Ctrl+P). Automatický shell tisk byl vypnut kvůli přetížení systému.");'''

replace_in_file('FullReportViewerViewModel.cs', old, new)

# ============================================================
# HistoryViewModel.cs - ShowConfirmationAsync with "Vymazat historii"
# ============================================================
old = '''            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Vymazat historii",
                "Opravdu chcete vymazat celou historii testů?");'''

new = '''            var confirmed = await _dialogService.ShowConfirmationAsync(
                L.Get("Common.Confirmation"),
                "Opravdu chcete vymazat celou historii testů?");'''

replace_in_file('HistoryViewModel.cs', old, new)

# ============================================================
# HistoryViewModel.cs - ShowConfirmationAsync with "Potvrzení"
# ============================================================
old = '''                var confirmation = await _dialogService.ShowConfirmationAsync(
                    "Potvrzení", 
                    $"Opravdu chcete smazat test '{SelectedTest.TestType}' z {SelectedTest.TestDate:dd.MM.yyyy HH:mm}?");'''

new = '''                var confirmation = await _dialogService.ShowConfirmationAsync(
                    L.Get("Common.Confirmation"), 
                    $"Opravdu chcete smazat test '{SelectedTest.TestType}' z {SelectedTest.TestDate:dd.MM.yyyy HH:mm}?");'''

replace_in_file('HistoryViewModel.cs', old, new)

# ============================================================
# ReportViewModel.cs - ShowConfirmationAsync with "Potvrzení"
# ============================================================
old = '''                var confirmation = await _dialogService.ShowConfirmationAsync(
                    "Potvrzení", 
                    $"Opravdu chcete smazat test \\"{SelectedReport.Title}\\" z {SelectedReport.TestDate:dd.MM.yyyy HH:mm}?");'''

new = '''                var confirmation = await _dialogService.ShowConfirmationAsync(
                    L.Get("Common.Confirmation"), 
                    $"Opravdu chcete smazat test \\"{SelectedReport.Title}\\" z {SelectedReport.TestDate:dd.MM.yyyy HH:mm}?");'''

replace_in_file('ReportViewModel.cs', old, new)

# ============================================================
# ReportViewModel.cs - ShowConfirmationAsync with "Certifikát vytvořen"
# ============================================================
old = '''                var openPdf = await _dialogService.ShowConfirmationAsync(
                    "Certifikát vytvořen",
                    $"Certifikát byl uložen do PDF:\\n{result.PdfPath}\\n\\nOtevřít soubor?");'''

new = '''                var openPdf = await _dialogService.ShowConfirmationAsync(
                    L.Get("Common.Confirmation"),
                    $"Certifikát byl uložen do PDF:\\n{result.PdfPath}\\n\\nOtevřít soubor?");'''

replace_in_file('ReportViewModel.cs', old, new)

# ============================================================
# ReportViewModel.cs - ShowConfirmationAsync with "Plný report připraven"
# ============================================================
old = '''                var openFile = await _dialogService.ShowConfirmationAsync(
                    "Plný report připraven",
                    (reducedMode ? "Report byl vytvořen v omezeném režimu kvůli nedostatku místa.\\n\\n" : string.Empty) +
                    $"Report byl uložen do:\\n{filePath}\\n\\n" +
                    (graphImagePath != null ? $"Graf (PNG):\\n{graphImagePath}\\n\\n" : string.Empty) +
                    "Otevřít report v aplikaci?");'''

new = '''                var openFile = await _dialogService.ShowConfirmationAsync(
                    L.Get("Common.Confirmation"),
                    (reducedMode ? "Report byl vytvořen v omezeném režimu kvůli nedostatku místa.\\n\\n" : string.Empty) +
                    $"Report byl uložen do:\\n{filePath}\\n\\n" +
                    (graphImagePath != null ? $"Graf (PNG):\\n{graphImagePath}\\n\\n" : string.Empty) +
                    "Otevřít report v aplikaci?");'''

replace_in_file('ReportViewModel.cs', old, new)

# ============================================================
# RestoreViewModel.cs - ShowConfirmationAsync with "⚠️ VAROVÁNÍ – Původní disk"
# ============================================================
old = '''            var confirmed = await _dialogService.ShowConfirmationAsync(
                "⚠️ VAROVÁNÍ – Původní disk",
                $"Vybraný cílový disk je PŮVODNÍ disk, ze kterého byla záloha vytvořena!\\n\\n" +
                $"Obnova na tento disk PŘEPÍŠE všechna data na něm.\\n\\n" +
                $"OPRAVDU chcete pokračovat?");'''

new = '''            var confirmed = await _dialogService.ShowConfirmationAsync(
                L.Get("Common.Warning"),
                $"Vybraný cílový disk je PŮVODNÍ disk, ze kterého byla záloha vytvořena!\\n\\n" +
                $"Obnova na tento disk PŘEPÍŠE všechna data na něm.\\n\\n" +
                $"OPRAVDU chcete pokračovat?");'''

replace_in_file('RestoreViewModel.cs', old, new)

# ============================================================
# RestoreViewModel.cs - ShowConfirmationAsync with "💾 Spustit obnovu"
# ============================================================
old = '''        var generalConfirm = await _dialogService.ShowConfirmationAsync(
            "💾 Spustit obnovu",
            $"Obnova bude provedena na disk: {targetDisk.DisplayName}\\n\\n" +
            $"Zdrojová záloha: {SelectedBackup.SourceModel}\\n" +
            $"Datum zálohy: {SelectedBackup.BackupDate}\\n" +
            $"Režim: {SelectedBackup.Mode}\\n" +
            $"Data: {SelectedBackup.TotalBytesText}\\n\\n" +
            $"OPRAVDU SPUSTIT OBNOVU?");'''

new = '''        var generalConfirm = await _dialogService.ShowConfirmationAsync(
            L.Get("Common.Confirmation"),
            $"Obnova bude provedena na disk: {targetDisk.DisplayName}\\n\\n" +
            $"Zdrojová záloha: {SelectedBackup.SourceModel}\\n" +
            $"Datum zálohy: {SelectedBackup.BackupDate}\\n" +
            $"Režim: {SelectedBackup.Mode}\\n" +
            $"Data: {SelectedBackup.TotalBytesText}\\n\\n" +
            $"OPRAVDU SPUSTIT OBNOVU?");'''

replace_in_file('RestoreViewModel.cs', old, new)

# ============================================================
# SeekTestViewModel.cs - ShowConfirmationAsync with "Vynucený seek test"
# ============================================================
old = '''            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Vynucený seek test na selhávajícím disku",
                "SMART/rekomendační modul označil disk jako příliš opotřebený nebo selhávající a výchozí doporučený počet seeků je 0.\\n\\n" +
                $"Požadujete vynucené spuštění {SelectedSeekCount} seeků proti doporučení. Test může disk dále zatížit nebo urychlit selhání.\\n\\n" +
                "Pokračovat pouze z diagnostických důvodů?");'''

new = '''            var confirmed = await _dialogService.ShowConfirmationAsync(
                L.Get("Common.Warning"),
                "SMART/rekomendační modul označil disk jako příliš opotřebený nebo selhávající a výchozí doporučený počet seeků je 0.\\n\\n" +
                $"Požadujete vynucené spuštění {SelectedSeekCount} seeků proti doporučení. Test může disk dále zatížit nebo urychlit selhání.\\n\\n" +
                "Pokračovat pouze z diagnostických důvodů?");'''

replace_in_file('SeekTestViewModel.cs', old, new)

# ============================================================
# SeekTestViewModel.cs - ShowConfirmationAsync with "Překročení bezpečného limitu"
# ============================================================
old = '''            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Překročení bezpečného limitu",
                $"Požadovaný počet seeků ({SelectedSeekCount}) překračuje bezpečný limit ({MaxSafeSeekCount}) " +
                $"doporučený na základě SMART dat.\\n\\nChcete přesto pokračovat?");'''

new = '''            var confirmed = await _dialogService.ShowConfirmationAsync(
                L.Get("Common.Warning"),
                $"Požadovaný počet seeků ({SelectedSeekCount}) překračuje bezpečný limit ({MaxSafeSeekCount}) " +
                $"doporučený na základě SMART dat.\\n\\nChcete přesto pokračovat?");'''

replace_in_file('SeekTestViewModel.cs', old, new)

# ============================================================
# SettingsViewModel.cs - ShowConfirmationAsync with "Potvrzení" (reset)
# ============================================================
old = '''                var confirmation = await _dialogService.ShowConfirmationAsync(
                    "Potvrzení", 
                    "Opravdu chcete resetovat všechna nastavení na výchozí hodnoty?");'''

new = '''                var confirmation = await _dialogService.ShowConfirmationAsync(
                    L.Get("Common.Confirmation"), 
                    "Opravdu chcete resetovat všechna nastavení na výchozí hodnoty?");'''

replace_in_file('SettingsViewModel.cs', old, new)

# ============================================================
# SettingsViewModel.cs - ShowConfirmationAsync with "Obnovení ze zálohy"
# ============================================================
old = '''                var confirmation = await _dialogService.ShowConfirmationAsync(
                    "Obnovení ze zálohy",
                    $"Opravdu chcete obnovit data ze zálohy?\\n\\n" +
                    $"Záloha: {SelectedBackup.FileName}\\n" +
                    $"Datum: {SelectedBackup.CreatedAt:dd.MM.yyyy HH:mm}\\n\\n" +
                    $"VAROVÁNÍ: Aktuální data budou přepsána!");'''

new = '''                var confirmation = await _dialogService.ShowConfirmationAsync(
                    L.Get("Common.Confirmation"),
                    $"Opravdu chcete obnovit data ze zálohy?\\n\\n" +
                    $"Záloha: {SelectedBackup.FileName}\\n" +
                    $"Datum: {SelectedBackup.CreatedAt:dd.MM.yyyy HH:mm}\\n\\n" +
                    $"VAROVÁNÍ: Aktuální data budou přepsána!");'''

replace_in_file('SettingsViewModel.cs', old, new)

# ============================================================
# SmartCheckViewModel.cs - ShowConfirmationAsync with "Sledovat průběh?"
# ============================================================
old = '''            var wantPolling = await _dialogService.ShowConfirmationAsync(
                "Sledovat průběh?",
                "Chcete sledovat průběh self-testu v reálném čase?\\n\\n" +
                "Aplikace bude pravidelně dotazovat SMART data pro zobrazení pokroku.");'''

new = '''            var wantPolling = await _dialogService.ShowConfirmationAsync(
                L.Get("Common.Confirmation"),
                "Chcete sledovat průběh self-testu v reálném čase?\\n\\n" +
                "Aplikace bude pravidelně dotazovat SMART data pro zobrazení pokroku.");'''

replace_in_file('SmartCheckViewModel.cs', old, new)

# ============================================================
# SurfaceTestViewModel.cs - ShowDangerConfirmationAsync with "Potvrzení sanitizace"
# ============================================================
old = '''         var confirmed = await _dialogService.ShowDangerConfirmationAsync(
             "Potvrzení sanitizace",
             $"Vybraný profil \\"{profile.Name}\\" přepíše obsah disku {SelectedDrive?.Name ?? "Neznámý"}.\\n\\n" +
             $"Tato operace je NEVRATNÁ a všechna data budou ztracena.\\n\\n" +
             $"OPRAVDU CHCETE POKRAČOVAT?");'''

new = '''         var confirmed = await _dialogService.ShowDangerConfirmationAsync(
             L.Get("Common.Confirmation"),
             $"Vybraný profil \\"{profile.Name}\\" přepíše obsah disku {SelectedDrive?.Name ?? "Neznámý"}.\\n\\n" +
             $"Tato operace je NEVRATNÁ a všechna data budou ztracena.\\n\\n" +
             $"OPRAVDU CHCETE POKRAČOVAT?");'''

replace_in_file('SurfaceTestViewModel.cs', old, new)

print("\n=== All replacements done! ===")
