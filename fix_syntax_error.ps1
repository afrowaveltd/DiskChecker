# Fix syntax error in SmartCheckService.cs line 163

Write-Host "=== Fixing Syntax Error in SmartCheckService.cs ===" -ForegroundColor Green

$file = "DiskChecker.Application\Services\SmartCheckService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Line 163 (before): $($lines[162])" -ForegroundColor Cyan

# Fix: Add missing comma
$lines[162] = '            Drive = null,  // CoreDriveInfo conversion needed'

Write-Host "Line 163 (after): $($lines[162])" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`nFixed syntax error" -ForegroundColor Green