$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\WindowsSmartaProvider.cs')
$lines = $content -split "`n"

Write-Output "=== GetSelfTestStatusAsync (around lines 295-330) ==="
for ($i = 294; $i -lt 340 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}