$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"
Write-Output "=== Last 30 lines ==="
for ($i = [Math]::Max(0, $lines.Count - 35); $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}