$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\WindowsSmartaProvider.cs')
$lines = $content -split "`n"

Write-Output "=== Searching for GetSelfTestStatusAsync ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'GetSelfTestStatusAsync') {
        Write-Output ""
        Write-Output "=== Found at line $($i+1) ==="
        for ($j = $i; $j -lt [Math]::Min($i+40, $lines.Count); $j++) {
            Write-Output "$($j+1): $($lines[$j])"
        }
        break
    }
}