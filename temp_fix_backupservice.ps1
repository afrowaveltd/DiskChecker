$utf8 = New-Object System.Text.UTF8Encoding $false

# 1. Opravit BackupService.cs
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Najít a opravit GetAvailableBackupsAsync
# Musí vracet Task<IEnumerable<IBackupService.BackupInfo>>

$lines = $content -split "`n"
$newLines = @()
$inMethod = $false
$braceCount = 0

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Najít GetAvailableBackupsAsync a změnit návratový typ
    if ($line -match "public async Task<IEnumerable<BackupInfo>> GetAvailableBackupsAsync") {
        $line = "    public async Task<IEnumerable<IBackupService.BackupInfo>> GetAvailableBackupsAsync()"
    }
    
    # Najít RestoreBackupAsync a změnit void na Task
    if ($line -match "public void RestoreBackupAsync") {
        $line = $line -replace "public void RestoreBackupAsync", "public async Task RestoreBackupAsync"
    }
    
    # BackupInfo -> IBackupService.BackupInfo v kódu
    if ($line -match "new BackupInfo" -and $line -notmatch "IBackupService.BackupInfo") {
        $line = $line -replace "new BackupInfo", "new IBackupService.BackupInfo"
    }
    
    # Odstranit duplicitní BackupInfo třídu na konci souboru
    if ($line -match "^public class BackupInfo" -or $line -match "^/// <summary" -and $i -gt 200) {
        break
    }
    
    $newLines += $line
}

$newContent = $newLines -join "`n"

# Přidat DeleteBackupAsync pokud chybí
if (-not ($newContent -match "DeleteBackupAsync")) {
    $newContent = $newContent.TrimEnd()
    $newContent += @"

    
    public async Task DeleteBackupAsync(string backupPath)
    {
        await Task.Run(() =>
        {
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
        });
    }
}
"@
}

[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "BackupService.cs fixed"