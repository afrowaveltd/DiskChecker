$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

Write-Output "=== First 40 lines ==="
for ($i = 0; $i -lt 40 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}

Write-Output ""
Write-Output "=== Last 10 lines ==="
for ($i = [Math]::Max(0, $lines.Count - 10); $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}