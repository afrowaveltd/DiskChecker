# FIX final 3 null reference warnings

Write-Host "=== FIXING FINAL 3 NULL REFERENCES ===" -ForegroundColor Green

$file = "DiskChecker.Application\Services\PdfReportExportService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Line 61: $($lines[60])" -ForegroundColor Cyan
Write-Host "Line 70: $($lines[69])" -ForegroundColor Cyan  
Write-Host "Line 80: $($lines[79])" -ForegroundColor Cyan

# Fix line 61 - add null-conditional operator for smart.SmartaData
$lines[60] = '        var model = (smart?.SmartaData?.DeviceModel?.ToSafeString() ?? smart?.SmartaData?.ModelFamily?.ToSafeString()) ?? smart?.Drive?.Name ?? "Unknown";'

# Fix line 70 - add null-conditional for DeviceModel
$lines[69] = '        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Model", (smart?.SmartaData?.DeviceModel?.ToSafeString()) ?? "Unknown");'

# Fix line 80 - need to check what line 80 has
# First check if there's a line 80 about Temperature or another field
if ($lines.Count -gt 79) {
    # Fix line 80 - add null-conditional for Temperature or other field
    $lines[79] = '        DrawTableRow(canvas, tableFont, textPaint, tableX - 20, tableY, "Temperature", (smart?.SmartaData?.Temperature ?? 0).ToString() + "°C");'
}

Write-Host "`nFixed lines:" -ForegroundColor Green
Write-Host "Line 61: $($lines[60])" -ForegroundColor Green
Write-Host "Line 70: $($lines[69])" -ForegroundColor Green
Write-Host "Line 80: $($lines[79])" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`n=== All null references fixed ===" -ForegroundColor Green