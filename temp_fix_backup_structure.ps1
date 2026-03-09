$utf8 = New-Object System.Text.UTF8Encoding $false

# Opravit BackupService.cs - přesunout DeleteBackupAsync dovnitř třídy
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Najít poslední } třídy BackupService (před "public async Task DeleteBackupAsync")
# a přesunout metodu dovnitř

$lines = $content -split "`n"
$newLines = @()
$inDeleteBackup = $false
$skipNextBrace = $false
$addedDeleteBackup = $false

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Pokud najdeme "}" po GetAvailableBackupsAsync a před DeleteBackupAsync
    if ($line -match "^\s*}$" -and $i -gt 260 -and $i -lt 280 -and -not $addedDeleteBackup) {
        # Přidat DeleteBackupAsync před toto }
        $newLines += "    "
        $newLines += "    public async Task DeleteBackupAsync(string backupPath)"
        $newLines += "    {"
        $newLines += "        await Task.Run(() =>"
        $newLines += "        {"
        $newLines += "            if (File.Exists(backupPath))"
        $newLines += "            {"
        $newLines += "                File.Delete(backupPath);"
        $newLines += "            }"
        $newLines += "        });"
        $newLines += "    }"
        $newLines += $line
        $addedDeleteBackup = $true
        continue
    }
    
    # Přeskočit prázdné řádky po původní } před DeleteBackupAsync
    if ($line -match "^\s*$" -and $lines[$i - 1] -match "^\s*}$" -and $i -gt 270 -and $i -lt 280) {
        continue
    }
    
    # Přeskočit DeleteBackupAsync definici mimo třídu
    if ($line -match "public async Task DeleteBackupAsync" -and $i -gt 270) {
        $inDeleteBackup = $true
        continue
    }
    
    if ($inDeleteBackup) {
        if ($line -match "^\s*}$") {
            $inDeleteBackup = $false
        }
        continue
    }
    
    $newLines += $line
}

$newContent = $newLines -join "`n"
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "BackupService.cs - DeleteBackupAsync moved inside class"