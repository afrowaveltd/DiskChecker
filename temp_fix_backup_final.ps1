$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Soubor má duplicitní zavírání třídy - musíme najít a opravit
$lines = $content -split "`n"
$newLines = @()
$skipUntilDeleteBackup = $false
$foundDeleteBackup = $false
$braceCount = 0

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Sledujeme počet závorek
    if ($line -match "^\s*{") { $braceCount++ }
    if ($line -match "^\s*}") { $braceCount-- }
    
    # Pokud najdeme "}" na řádku 273 a následuje DeleteBackupAsync, přeskočíme první }
    if ($line -match "^\s*}$" -and $i -gt 265 -and $i -lt 280 -and -not $foundDeleteBackup) {
        # Kontrolujeme, zda následuje DeleteBackupAsync
        if ($i + 1 -lt $lines.Count -and $lines[$i + 1] -match "DeleteBackupAsync") {
            # Přeskočíme tuto závorku
            continue
        }
    }
    
    $newLines += $line
}

$newContent = $newLines -join "`n"
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "BackupService.cs fixed"
Write-Output "Lines: $($newLines.Count)"