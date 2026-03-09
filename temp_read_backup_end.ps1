$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs", $utf8)
$lines = $content -split "`n"
Write-Output "=== BackupService.cs last 50 lines ==="
$start = [Math]::Max(0, $lines.Count - 50)
for ($i = $start; $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}