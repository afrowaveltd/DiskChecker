$utf8 = New-Object System.Text.UTF8Encoding $false
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Models\SmartaData.cs"
$prevContent = [System.IO.File]::ReadAllText($prevPath, $utf8)
$prevLines = $prevContent -split "`n"

Write-Output "=== SmartaSelfTestStatus in Previous ==="
for ($i = 0; $i -lt $prevLines.Count; $i++) {
    if ($prevLines[$i] -match "enum SmartaSelfTestStatus|class SmartaSelfTestStatus") {
        for ($j = $i; $j -lt [Math]::Min($i + 20, $prevLines.Count); $j++) {
            Write-Output "$($j+1): $($prevLines[$j])"
        }
        break
    }
}