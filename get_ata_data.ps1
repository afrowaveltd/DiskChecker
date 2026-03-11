$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs')
$lines = $content -split "`n"

Write-Output "=== ParseAtaData method (lines 139-276) ==="
for ($i = 138; $i -lt 276; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}