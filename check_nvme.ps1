$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs')
$lines = $content -split "`n"

Write-Output "=== ParseNvmeData - lines 485-550 ==="
for ($i = 484; $i -lt 560; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}