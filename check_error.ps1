# Check the error around line 273
$backupServicePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($backupServicePath)
$lines = $content -split "`n"

Write-Host "Lines 260-280:"
for ($i = 259; $i -lt 280 -and $i -lt $lines.Count; $i++) {
    Write-Host "$($i+1): $($lines[$i])"
}