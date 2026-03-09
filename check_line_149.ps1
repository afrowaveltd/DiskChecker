# Check exact problem on line 149 in SmartctlJsonParser.cs

Write-Host "=== Checking SmartctlJsonParser.cs Line 149 ===" -ForegroundColor Yellow

$file = "DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "`nContext around line 149:" -ForegroundColor Cyan
for ($i = 145; $i -lt [Math]::Min(155, $lines.Count); $i++) {
    $lineNum = $i + 1
    if ($lineNum -eq 149) {
        Write-Host "$lineNum**`t$($lines[$i])" -ForegroundColor Red
    } else {
        Write-Host "$lineNum`t$($lines[$i])" -ForegroundColor White
    }
}