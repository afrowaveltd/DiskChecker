$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs')
$lines = $content -split "`n"

Write-Output "=== ParseAtaData method - CurrentSelfTest handling ==="
for ($i = 140; $i -lt 280 -and $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'CurrentSelfTest|ParseAtaData|ParseNvmeData') {
        Write-Output ""
        Write-Output "=== Found at line $($i+1) ==="
        for ($j = [Math]::Max(0, $i-2); $j -lt [Math]::Min($i+30, $lines.Count); $j++) {
            Write-Output "$($j+1): $($lines[$j])"
        }
    }
}