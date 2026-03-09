$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

Write-Output "=== First 5 lines ==="
for ($i = 0; $i -lt 5 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}

Write-Output ""
Write-Output "=== Checking for namespace ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "namespace|^}$") {
        Write-Output "$($i+1): $($lines[$i])"
    }
}

# Count braces
$openBraces = 0
$closeBraces = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    $openBraces += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
    $closeBraces += ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
}
Write-Output ""
Write-Output "Open braces: $openBraces"
Write-Output "Close braces: $closeBraces"