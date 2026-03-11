$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs')
$lines = $content -split "`n"

Write-Output "=== Lines 270-320 (Fallback checks) ==="
for ($i = 269; $i -lt 320; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}