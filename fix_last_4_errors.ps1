# FINAL - Fix last 4 errors in PdfReportExportService.cs

Write-Host "=== FINAL FIX - Last 4 Errors ===" -ForegroundColor Green

$file = "DiskChecker.Application\Services\PdfReportExportService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Current errors:" -ForegroundColor Yellow
Write-Host "Line 76: $($lines[75])" -ForegroundColor Cyan
Write-Host "Line 85: $($lines[84])" -ForegroundColor Cyan
Write-Host "Line 88: $($lines[87])" -ForegroundColor Cyan
Write-Host "Line 97: $($lines[96])" -ForegroundColor Cyan

# Fix line 76 - null reference for PowerOnHours
$lines[75] = '        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Power On Hours", $"{(smart?.SmartaData?.PowerOnHours ?? 0)}h");'

# Fix line 85 - bool? to bool conversion
$lines[84] = '        var isHealthy = smart?.SmartaData?.IsHealthy ?? true;'

# Fix line 88 - nullable value can't be null
$lines[87] = '        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Wear Level", $"{(smart?.SmartaData?.WearLevelingCount ?? 0)}%");'

# Fix line 97 - null reference for Rating
$lines[96] = '        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Rating", smart?.Rating.GetDescription() ?? "Unknown");'

Write-Host "`nFixed lines:" -ForegroundColor Green
Write-Host "Line 76: $($lines[75])" -ForegroundColor Green
Write-Host "Line 85: $($lines[84])" -ForegroundColor Green
Write-Host "Line 88: $($lines[87])" -ForegroundColor Green
Write-Host "Line 97: $($lines[96])" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`n=== All 4 errors fixed ===" -ForegroundColor Green
Write-Host "Final build should succeed!" -ForegroundColor Yellow