$content = [System.IO.File]::ReadAllText('DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs')
$lines = $content -split "`n"

Write-Output "=== ParseAtaSelfTestLog and ParseTestStatus methods ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'ParseTestStatus|ParseAtaSelfTestLog|private static SmartaSelfTestStatus') {
        Write-Output ""
        Write-Output "=== Found at line $($i+1) ==="
        for ($j = [Math]::Max(0, $i-2); $j -lt [Math]::Min($i+35, $lines.Count); $j++) {
            Write-Output "$($j+1): $($lines[$j])"
        }
    }
}