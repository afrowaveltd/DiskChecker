import os
import re

viewmodels_dir = r'K:\Afrowave Projects\DiskChecker\DiskChecker.UI.Avalonia\ViewModels'

# Define all replacements as (filename, old_pattern, new_pattern) tuples
# Using raw strings to avoid encoding issues

replacements = [
    # AnalysisViewModel.cs
    ('AnalysisViewModel.cs', 
     'ShowErrorAsync("Analýza", StatusMessage)',
     'ShowErrorAsync(L.Get("Common.Error"), StatusMessage)'),
    
    # BackupViewModel.cs
    ('BackupViewModel.cs',
     '_dialogService.ShowErrorAsync("Záloha běží", "Nelze opustit během zálohování. Nejprve zálohu přerušte.")',
     '_dialogService.ShowErrorAsync(L.Get("Common.BackupRunning"), L.Get("Common.CannotLeaveDuringBackup"))'),
    
    ('BackupViewModel.cs',
     'ShowErrorAsync("RAW záloha nenalezena", "Nejdříve vytvořte RAW zálohu.")',
     'ShowErrorAsync(L.Get("Common.RawBackupNotFound"), L.Get("Common.RawBackupNotFoundMsg"))'),
    
    ('BackupViewModel.cs',
     'ShowErrorAsync("Převod RAW → VHDx selhal", ex.Message)',
     'ShowErrorAsync(L.Get("Common.RawToVhdxFailed"), ex.Message)'),
    
    # CertificateBrowserViewModel.cs
    ('CertificateBrowserViewModel.cs',
     'ShowInfoAsync("PDF Export"',
     'ShowInfoAsync(L.Get("CertificateView.Dialog.PdfExport")'),
    
    # DiskComparisonViewModel.cs
    ('DiskComparisonViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se načíst disky:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.LoadFailed"),'),
    
    ('DiskComparisonViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se porovnat disky:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.CompareFailed"),'),
    
    ('DiskComparisonViewModel.cs',
     'ShowMessageAsync("Porovnání výkonu"',
     'ShowMessageAsync(L.Get("Common.Comparison")'),
    
    # DiskSelectionViewModel.cs
    ('DiskSelectionViewModel.cs',
     'ShowInfoAsync("Žádná historie", "Tento disk ještě nemá uloženou kartu.")',
     'ShowInfoAsync(L.Get("Common.NoHistory"), L.Get("Common.DiskNoCard"))'),
    
    ('DiskSelectionViewModel.cs',
     'ShowInfoAsync("Žádná historie", "Tento disk ještě nebyl testován.")',
     'ShowInfoAsync(L.Get("Common.NoHistory"), L.Get("Common.DiskNotTested"))'),
    
    # FullReportViewerViewModel.cs
    ('FullReportViewerViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se načíst report:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.LoadFailed"),'),
    
    ('FullReportViewerViewModel.cs',
     'ShowWarningAsync("Tisk", "Report není dostupný pro tisk.")',
     'ShowWarningAsync(L.Get("Common.Print"), L.Get("Common.PrintNotAvailable"))'),
    
    ('FullReportViewerViewModel.cs',
     'ShowInfoAsync("Tisk"',
     'ShowInfoAsync(L.Get("Common.Print")'),
    
    ('FullReportViewerViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se otevřít report pro tisk:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.OpenFailed"),'),
    
    ('FullReportViewerViewModel.cs',
     'ShowWarningAsync("Report", "Report není dostupný.")',
     'ShowWarningAsync(L.Get("Common.Report"), L.Get("Common.ReportNotAvailable"))'),
    
    ('FullReportViewerViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se otevřít report:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.OpenFailed"),'),
    
    # HistoryViewModel.cs
    ('HistoryViewModel.cs',
     'ShowMessageAsync("Detaily testu"',
     'ShowMessageAsync(L.Get("History.DetailTitle")'),
    
    # ReportViewModel.cs
    ('ReportViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se smazat test:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.DeleteFailed"),'),
    
    ('ReportViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se exportovat certifikát:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.CertExportFailed"),'),
    
    ('ReportViewModel.cs',
     'ShowErrorAsync("Chyba", "Karta disku pro vybraný report nebyla nalezena.")',
     'ShowErrorAsync(L.Get("Common.Error"), L.Get("Common.CardNotFound"))'),
    
    ('ReportViewModel.cs',
     'ShowErrorAsync("Chyba", "Testová session pro vybraný report nebyla nalezena.")',
     'ShowErrorAsync(L.Get("Common.Error"), L.Get("Common.CardNotFound"))'),
    
    ('ReportViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se vygenerovat plný report:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.SaveFailed"),'),
    
    ('ReportViewModel.cs',
     'ShowWarningAsync("Report", "Nejdříve vygenerujte plný report.")',
     'ShowWarningAsync(L.Get("Common.Report"), L.Get("Common.ReportNotGenerated"))'),
    
    ('ReportViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se načíst testy:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.LoadFailed"),'),
    
    # RestoreViewModel.cs
    ('RestoreViewModel.cs',
     'ShowErrorAsync("Chyba", "Není vybrán cílový disk pro obnovu.")',
     'ShowErrorAsync(L.Get("Common.Error"), L.Get("Common.TargetDiskNotSelected"))'),
    
    ('RestoreViewModel.cs',
     'ShowConfirmationAsync("Cílový disk je mírně menší"',
     'ShowConfirmationAsync(L.Get("Common.TargetDiskSlightlySmaller")'),
    
    ('RestoreViewModel.cs',
     'ShowErrorAsync("Cílový disk je malý"',
     'ShowErrorAsync(L.Get("Common.TargetDiskSmall")'),
    
    ('RestoreViewModel.cs',
     'ShowErrorAsync("Obnova běží", "Nelze opustit během obnovy. Nejprve obnovu přerušte.")',
     'ShowErrorAsync(L.Get("Common.RestoreRunning"), L.Get("Common.CannotLeaveDuringRestore"))'),
    
    # SafeDestructiveTestViewModel.cs
    ('SafeDestructiveTestViewModel.cs',
     'ShowErrorAsync("Operace běží", "Nelze opustit během běžící operace. Nejprve operaci přerušte.")',
     'ShowErrorAsync(L.Get("Common.OperationRunning"), L.Get("Common.CannotLeaveDuringOperation"))'),
    
    # SettingsViewModel.cs
    ('SettingsViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se načíst nastavení:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.SettingsLoadFailed"),'),
    
    ('SettingsViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se uložit nastavení:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.SettingsSaveFailed"),'),
    
    ('SettingsViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se resetovat nastavení:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.SettingsResetFailed"),'),
    
    ('SettingsViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se vybrat složku:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.FolderSelectFailed"),'),
    
    ('SettingsViewModel.cs',
     'ShowErrorAsync("Databáze"',
     'ShowErrorAsync(L.Get("Common.Database")'),
    
    ('SettingsViewModel.cs',
     'ShowMessageAsync("Záloha vytvořena"',
     'ShowMessageAsync(L.Get("Common.BackupCreated")'),
    
    ('SettingsViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se vytvořit zálohu:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.BackupCreateFailed"),'),
    
    ('SettingsViewModel.cs',
     'ShowMessageAsync("Obnovení dokončeno"',
     'ShowMessageAsync(L.Get("Common.RestoreCompleted")'),
    
    ('SettingsViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se obnovit zálohu:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.BackupRestoreFailed"),'),
    
    # SmartCheckViewModel.cs
    ('SmartCheckViewModel.cs',
     'ShowErrorAsync("Chyba", "Nepodařilo se načíst disky:',
     'ShowErrorAsync(L.Get("Common.Error"), string.Format(L.Get("Common.LoadFailed"),'),
    
    ('SmartCheckViewModel.cs',
     'ShowErrorAsync("Upozornění"',
     'ShowErrorAsync(L.Get("Common.Warning")'),
    
    ('SmartCheckViewModel.cs',
     'ShowErrorAsync("Nepodporováno"',
     'ShowErrorAsync(L.Get("Common.Unsupported")'),
    
    ('SmartCheckViewModel.cs',
     'ShowConfirmationAsync("Potvrzení"',
     'ShowConfirmationAsync(L.Get("Common.Confirmation")'),
    
    # SurfaceTestViewModel.cs
    ('SurfaceTestViewModel.cs',
     'ShowErrorAsync("Sanitizace dokončena"',
     'ShowErrorAsync(L.Get("SurfaceTest.Status.SanitizeCompleted")'),
    
    ('SurfaceTestViewModel.cs',
     'ShowErrorAsync("Chyba sanitizace"',
     'ShowErrorAsync(L.Get("SurfaceTest.Status.SanitizeError")'),
    
    ('SurfaceTestViewModel.cs',
     'ShowErrorAsync("Test dokončen"',
     'ShowErrorAsync(L.Get("SurfaceTest.Status.TestCompleted")'),
]

# Also handle the "Chyba" standalone title replacements (ShowErrorAsync("Chyba", ...))
# These need to be done carefully to not conflict with already-replaced ones

for filename, old, new in replacements:
    filepath = os.path.join(viewmodels_dir, filename)
    if not os.path.exists(filepath):
        print(f"SKIP: {filename} not found")
        continue
    
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    count = content.count(old)
    if count > 0:
        content = content.replace(old, new)
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"OK: {filename} - replaced {count} occurrence(s)")
    else:
        print(f"NOT FOUND: {filename} - pattern not found: {old[:60]}...")

print("\n=== Phase 2: Remaining 'Chyba' titles ===")

# Now handle remaining ShowErrorAsync("Chyba", ...) that weren't caught above
# These are the ones where message is ex.Message or StatusMessage (dynamic)
for filename in os.listdir(viewmodels_dir):
    if not filename.endswith('.cs'):
        continue
    filepath = os.path.join(viewmodels_dir, filename)
    
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Replace ShowErrorAsync("Chyba", ex.Message) -> ShowErrorAsync(L.Get("Common.Error"), ex.Message)
    # But only if not already replaced (not containing L.Get)
    import re
    
    # Pattern: ShowErrorAsync("Chyba", ...) where ... doesn't contain L.Get
    pattern = r'ShowErrorAsync\("Chyba",\s*((?!L\.Get)[^)]+)\)'
    
    def replace_chyba(match):
        msg = match.group(1).strip()
        return f'ShowErrorAsync(L.Get("Common.Error"), {msg})'
    
    new_content = re.sub(pattern, replace_chyba, content)
    
    if new_content != content:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(new_content)
        print(f"OK: {filename} - replaced remaining 'Chyba' titles")

print("\nDone!")
