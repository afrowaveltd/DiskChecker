# Check Application Project Errors

Write-Host "=== Checking Application Project Errors ===" -ForegroundColor Green

# 1. Check QualityRating usage in DiskCheckerService.cs
Write-Host "`n1. DiskCheckerService.cs Line 28:" -ForegroundColor Yellow
$file1 = "DiskChecker.Application\Services\DiskCheckerService.cs"
$lines1 = Get-Content $file1 -Encoding UTF8
Write-Host "$($lines1[27])" -ForegroundColor Cyan

# 2. Check DriveInfo.Path usage
Write-Host "`n2. TestReportExportService.cs Line 25:" -ForegroundColor Yellow
$file2 = "DiskChecker.Application\Services\TestReportExportService.cs"
$lines2 = Get-Content $file2 -Encoding UTF8
Write-Host "$($lines2[24])" -ForegroundColor Cyan

# 3. Check int? ?? string issues
Write-Host "`n3. SmartCheckService.cs Lines 81, 93-94:" -ForegroundColor Yellow
$file3 = "DiskChecker.Application\Services\SmartCheckService.cs"
$lines3 = Get-Content $file3 -Encoding UTF8
Write-Host "Line 81: $($lines3[80])" -ForegroundColor Cyan
Write-Host "Line 93: $($lines3[92])" -ForegroundColor Cyan
Write-Host "Line 94: $($lines3[93])" -ForegroundColor Cyan

# 4. Check CoreDriveInfo to DriveInfo conversion
Write-Host "`n4. SmartCheckService.cs Line 163:" -ForegroundColor Yellow
Write-Host "$($lines3[162])" -ForegroundColor Cyan

Write-Host "`n=== Summary of Issues ===" -ForegroundColor Green
Write-Host "- QualityRating.Grade/Score need extension method calls" -ForegroundColor Red
Write-Host "- DriveInfo.Path doesn't exist, need alternative" -ForegroundColor Red
Write-Host "- int? ?? string needs ToSafeString()" -ForegroundColor Red
Write-Host "- CoreDriveInfo to DriveInfo conversion needed" -ForegroundColor Red