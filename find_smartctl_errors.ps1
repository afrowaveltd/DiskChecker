# Find exact errors in SmartctlJsonParser.cs - 4 errors

Write-Host "=== Finding SmartctlJsonParser.cs Errors ===" -ForegroundColor Green

$file = "DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "`nLine 140 (CS8601 - null reference):" -ForegroundColor Yellow
$lines[139]

Write-Host "`nLine 149 (CS8601 - null reference):" -ForegroundColor Yellow
$lines[148]

Write-Host "`nLine 254 (CS0029 - string to int?):" -ForegroundColor Yellow
$lines[253]

Write-Host "`nLine 255 (CS8601 - null reference):" -ForegroundColor Yellow
$lines[254]

# Context around each error
Write-Host "`n=== Context around line 140-150 ===" -ForegroundColor Cyan
for ($i = 138; $i -lt [Math]::Min(152, $lines.Count); $i++) {
    $lineNum = $i + 1
    Write-Host "$lineNum`t$($lines[$i])"
}

Write-Host "`n=== Context around line 250-260 ===" -ForegroundColor Cyan
for ($i = 249; $i -lt [Math]::Min(262, $lines.Count); $i++) {
    $lineNum = $i + 1
    Write-Host "$lineNum`t$($lines[$i])"
}