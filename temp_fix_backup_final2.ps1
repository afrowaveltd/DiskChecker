$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Odstranit duplicitní konec - řádky 285-287
$lines = $content -split "`n"
$newLines = @()

for ($i = 0; $i -lt 284; $i++) {
    if ($i -lt $lines.Count) {
        $newLines += $lines[$i]
    }
}

$newContent = $newLines -join "`n"
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)

# Zkontroluj výsledek
$content2 = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines2 = $content2 -split "`n"
Write-Output "Fixed. New line count: $($lines2.Count)"
Write-Output "=== Last 10 lines ==="
for ($i = [Math]::Max(0, $lines2.Count - 10); $i -lt $lines2.Count; $i++) {
    Write-Output "$($i+1): $($lines2[$i])"
}