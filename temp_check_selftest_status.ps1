$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

Write-Output "=== SmartaSelfTestStatus definition ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "class SmartaSelfTestStatus") {
        for ($j = $i; $j -lt [Math]::Min($i + 20, $lines.Count); $j++) {
            Write-Output "$($j+1): $($lines[$j])"
        }
        break
    }
}