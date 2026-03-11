$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs')
$lines = $content -split "`n"

Write-Output "=== ParseNvmeSelfTestLog - lines 565-650 ==="
for ($i = 564; $i -lt 650; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}