$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

Write-Output "=== Lines 200-246 ==="
for ($i = 199; $i -lt 246 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}

Write-Output ""
Write-Output "=== Class/struct definitions ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^public (class|static class|struct|enum) ") {
        Write-Output "$($i+1): $($lines[$i])"
    }
    if ($lines[$i] -match "^}") {
        Write-Output "$($i+1): $($lines[$i])"
    }
}