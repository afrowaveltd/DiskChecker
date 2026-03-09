# FIX syntax error on line 78

Write-Host "=== FIXING SYNTAX ERROR LINE 78 ===" -ForegroundColor Green

$file = "DiskChecker.Application\Services\PdfReportExportService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Line 78 (before): $($lines[77])" -ForegroundColor Cyan

# Fix the syntax error - remove the extra parenthesis
$lines[77] = '        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Reallocated", (smart.SmartaData?.ReallocatedSectorCount ?? 0).ToString());'

Write-Host "Line 78 (after): $($lines[77])" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "Fixed syntax error!" -ForegroundColor Green