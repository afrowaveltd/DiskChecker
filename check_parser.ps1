$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs')
$lines = $content -split "`n"

Write-Output "=== First 30 lines (usings) ==="
for ($i = 0; $i -lt 30 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}

Write-Output ""
Write-Output "=== Lines around Parse method (lines 160-250) ==="
for ($i = 159; $i -lt 250 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}