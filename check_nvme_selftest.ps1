$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs')
$lines = $content -split "`n"

Write-Output "=== ParseNvmeData - CurrentSelfTest for NVMe ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'ParseNvmeData|nvme_smart|nvme_self_test') {
        Write-Output ""
        Write-Output "=== Found at line $($i+1) ==="
        for ($j = [Math]::Max(0, $i-2); $j -lt [Math]::Min($i+50, $lines.Count); $j++) {
            Write-Output "$($j+1): $($lines[$j])"
        }
    }
}