$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

Write-Output "=== Lines 215-248 ==="
for ($i = 214; $i -lt 248 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}

Write-Output ""
Write-Output "=== Looking for class definitions ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^public (class|static class|enum|struct)") {
        Write-Output "$($i+1): $($lines[$i])"
    }
}