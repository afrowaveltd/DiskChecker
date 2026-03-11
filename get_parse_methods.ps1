$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs')
$lines = $content -split "`n"

Write-Output "=== Lines 65-145 (Parse continuation and branching) ==="
for ($i = 64; $i -lt 145; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}