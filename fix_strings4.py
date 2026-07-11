import os
import re

viewmodels_dir = r'K:\Afrowave Projects\DiskChecker\DiskChecker.UI.Avalonia\ViewModels'

def replace_in_file(filename, replacements):
    """Apply list of (old, new) replacements to a file."""
    filepath = os.path.join(viewmodels_dir, filename)
    if not os.path.exists(filepath):
        print(f"SKIP: {filename} not found")
        return
    
    with open(filepath, 'rb') as f:
        raw = f.read()
    
    modified = False
    for old_bytes, new_bytes in replacements:
        if old_bytes in raw:
            raw = raw.replace(old_bytes, new_bytes)
            modified = True
            print(f"  Replaced in {filename}")
        else:
            print(f"  NOT FOUND in {filename}")
    
    if modified:
        with open(filepath, 'wb') as f:
            f.write(raw)

# All replacements as bytes (to handle encoding properly)
replacements = {
    # BackupViewModel.cs
    'BackupViewModel.cs': [
        # ShowConfirmationAsync with "💾 Spustit zálohování"
        (b'ShowConfirmationAsync(\r\n            "\xf0\x9f\x92\xbe Spustit z\xc3\xa1lohov\xc3\xa1n\xc3\xad"',
         b'ShowConfirmationAsync(\r\n            L.Get("Common.Confirmation")'),
    ],
    
    # FullReportViewerViewModel.cs
    'FullReportViewerViewModel.cs': [
        (b'ShowInfoAsync(\r\n                "Tisk"',
         b'ShowInfoAsync(\r\n                L.Get("Common.Print")'),
    ],
    
    # HistoryViewModel.cs
    'HistoryViewModel.cs': [
        (b'ShowConfirmationAsync(\r\n                "Vymazat historii"',
         b'ShowConfirmationAsync(\r\n                L.Get("Common.Confirmation")'),
        (b'ShowConfirmationAsync(\r\n                    "Potvrzen\xc3\xad"',
         b'ShowConfirmationAsync(\r\n                    L.Get("Common.Confirmation")'),
    ],
    
    # ReportViewModel.cs
    'ReportViewModel.cs': [
        (b'ShowConfirmationAsync(\r\n                    "Potvrzen\xc3\xad"',
         b'ShowConfirmationAsync(\r\n                    L.Get("Common.Confirmation")'),
        (b'ShowConfirmationAsync(\r\n                    "Certifik\xc3\xa1t vytvo\xc5\x99en"',
         b'ShowConfirmationAsync(\r\n                    L.Get("Common.Confirmation")'),
        (b'ShowConfirmationAsync(\r\n                    "Pln\xc3\xbd report p\xc5\x99ipraven"',
         b'ShowConfirmationAsync(\r\n                    L.Get("Common.Confirmation")'),
    ],
    
    # RestoreViewModel.cs
    'RestoreViewModel.cs': [
        (b'ShowConfirmationAsync(\r\n                "\xe2\x9a\xa0\xef\xb8\x8f VAROV\xc3\x81N\xc3\x8d \xe2\x80\x93 P\xc5\xafvodn\xc3\xad disk"',
         b'ShowConfirmationAsync(\r\n                L.Get("Common.Warning")'),
        (b'ShowConfirmationAsync(\r\n            "\xf0\x9f\x92\xbe Spustit obnovu"',
         b'ShowConfirmationAsync(\r\n            L.Get("Common.Confirmation")'),
    ],
    
    # SeekTestViewModel.cs
    'SeekTestViewModel.cs': [
        (b'ShowConfirmationAsync(\r\n                "Vynucen\xc3\xbd seek test na selh\xc3\xa1vaj\xc3\xadc\xc3\xadm disku"',
         b'ShowConfirmationAsync(\r\n                L.Get("Common.Warning")'),
        (b'ShowConfirmationAsync(\r\n                "P\xc5\x99ekro\xc4\x8den\xc3\xad bezpe\xc4\x8dn\xc3\xa9ho limitu"',
         b'ShowConfirmationAsync(\r\n                L.Get("Common.Warning")'),
    ],
    
    # SettingsViewModel.cs
    'SettingsViewModel.cs': [
        (b'ShowConfirmationAsync(\r\n                    "Potvrzen\xc3\xad"',
         b'ShowConfirmationAsync(\r\n                    L.Get("Common.Confirmation")'),
        (b'ShowConfirmationAsync(\r\n                    "Obnoven\xc3\xad ze z\xc3\xa1lohy"',
         b'ShowConfirmationAsync(\r\n                    L.Get("Common.Confirmation")'),
    ],
    
    # SmartCheckViewModel.cs
    'SmartCheckViewModel.cs': [
        (b'ShowConfirmationAsync(\r\n            "Sledovat pr\xc5\xafb\xc4\x9bh?"',
         b'ShowConfirmationAsync(\r\n            L.Get("Common.Confirmation")'),
    ],
    
    # SurfaceTestViewModel.cs
    'SurfaceTestViewModel.cs': [
        (b'ShowDangerConfirmationAsync(\r\n             "Potvrzen\xc3\xad sanitizace"',
         b'ShowDangerConfirmationAsync(\r\n             L.Get("Common.Confirmation")'),
    ],
}

for filename, reps in replacements.items():
    replace_in_file(filename, reps)

print("\nDone!")
