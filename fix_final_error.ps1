# Fix last compilation error in SmartctlJsonParser.cs line 149

Write-Host "=== Fixing Final Error ===" -ForegroundColor Green

$file = "DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "`nCurrent line 149:" -ForegroundColor Yellow
Write-Host "$($lines[148])" -ForegroundColor Cyan

# Fix null reference warning on line 149
$lines[148] = '                    WhenFailed = item.TryGetProperty("when_failed", out var wf) && wf.ValueKind != JsonValueKind.Null '

Write-Host "`nFixed line 149: Added proper null check for WhenFailed" -ForegroundColor Green

# Check line 150 if it needs update too
Write-Host "`nCurrent line 150:" -ForegroundColor Yellow
Write-Host "$($lines[149])" -ForegroundColor Cyan

# Make sure the continuation is correct
if ($lines[149] -match "wf.GetString\(\) \?\?") {
    Write-Host "Line 150 already has null-coalescing operator" -ForegroundColor Green
} else {
    $lines[149] = '                        ? wf.GetString() ?? string.Empty '
    Write-Host "Fixed line 150: Added null-coalescing for GetString()" -ForegroundColor Green
}

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`n=== Fixed last error ===" -ForegroundColor Green