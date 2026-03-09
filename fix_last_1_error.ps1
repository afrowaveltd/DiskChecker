# FINAL - Fix last 1 error

Write-Host "=== FIXING LAST ERROR ===" -ForegroundColor Green

$file = "DiskChecker.Application\Services\PdfReportExportService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Line 78 (before): $($lines[77])" -ForegroundColor Cyan

# Fix line 78 - add null-conditional for ReallocatedSectorCount
$lines[77] = '        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Reallocated", $"{smart?.SmartaData?.ReallocatedSectorCount ?? 0}");'

Write-Host "Line 78 (after): $($lines[77])" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`n=== Last error fixed ===" -ForegroundColor Green
Write-Host "BUILD SHOULD NOW SUCCEED!" -ForegroundColor Yellow