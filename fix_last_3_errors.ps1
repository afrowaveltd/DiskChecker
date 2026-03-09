# FINAL 3 ERRORS FIX

Write-Host "=== FIXING LAST 3 ERRORS ===" -ForegroundColor Green

# 1. Fix TestReportExportService.cs line 153 - ToString(CultureInfo)
Write-Host "`n1. Fixing TestReportExportService.cs line 153..." -ForegroundColor Yellow
$file1 = "DiskChecker.Application\Services\TestReportExportService.cs"
$lines1 = [System.IO.File]::ReadAllLines($file1, [System.Text.Encoding]::UTF8)

Write-Host "Line 153 (before): $($lines1[152])" -ForegroundColor Cyan

# Fix: Remove CultureInfo argument from ToString()
$lines1[152] = $lines1[152] -replace '\.ToString\(CultureInfo\.InvariantCulture\)', '.ToString()'

Write-Host "Line 153 (after): $($lines1[152])" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file1, $lines1, [System.Text.Encoding]::UTF8)
Write-Host "  Fixed TestReportExportService.cs" -ForegroundColor Green

# 2. Fix PdfReportExportService.cs lines 61, 76 - null references
Write-Host "`n2. Fixing PdfReportExportService.cs..." -ForegroundColor Yellow
$file2 = "DiskChecker.Application\Services\PdfReportExportService.cs"
$lines2 = [System.IO.File]::ReadAllLines($file2, [System.Text.Encoding]::UTF8)

Write-Host "Line 61 (before): $($lines2[60])" -ForegroundColor Cyan
Write-Host "Line 76 (before): $($lines2[75])" -ForegroundColor Cyan

# Line 61: Add null check for smart.SmartaData
$lines2[60] = '        var model = (smart.SmartaData?.DeviceModel?.ToSafeString() ?? smart.SmartaData?.ModelFamily?.ToSafeString()) ?? smart.Drive?.Name ?? "Unknown";'

# Line 76: Add null coalescing for PowerOnHours.ToString()
$lines2[75] = '        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Power On Hours", (smart.SmartaData?.PowerOnHours ?? 0).ToString());'

Write-Host "Line 61 (after): $($lines2[60])" -ForegroundColor Green
Write-Host "Line 76 (after): $($lines2[75])" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file2, $lines2, [System.Text.Encoding]::UTF8)
Write-Host "  Fixed PdfReportExportService.cs" -ForegroundColor Green

Write-Host "`n=== All 3 errors fixed ===" -ForegroundColor Green
Write-Host "Run dotnet build to verify compilation succeeds" -ForegroundColor Yellow