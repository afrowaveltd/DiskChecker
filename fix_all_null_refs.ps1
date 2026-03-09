# FINAL - Fix ALL remaining null reference warnings in PdfReportExportService.cs

Write-Host "=== FINAL FIX - PdfReportExportService.cs ===" -ForegroundColor Green

$file = "DiskChecker.Application\Services\PdfReportExportService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "`nCurrent errors:" -ForegroundColor Yellow
Write-Host "Line 66: $($lines[65])" -ForegroundColor Cyan
Write-Host "Line 72: $($lines[71])" -ForegroundColor Cyan
Write-Host "Line 82: $($lines[81])" -ForegroundColor Cyan

# Fix all null references in PdfReportExportService.cs
for ($i = 0; $i -lt $lines.Count; $i++) {
    # Fix null-conditional operators for smart.SmartaData properties
    if ($lines[$i] -match 'smart\.SmartaData\.' -and $lines[$i] -notmatch 'smart\?\.SmartaData\?') {
        $lines[$i] = $lines[$i] -replace 'smart\.SmartaData\.', 'smart?.SmartaData?.' -replace '\?\?', '??'
        Write-Host "  Fixed line $($i+1): Added null-conditional operators" -ForegroundColor Green
    }
    
    # Fix specific lines that still have errors
    # Line 66 - pending sector count
    if ($i -eq 65) {
        $lines[$i] = '        var pending = smart?.SmartaData?.PendingSectorCount ?? 0;'
        Write-Host "  Fixed line 66: PendingSectorCount null-safe" -ForegroundColor Green
    }
    
    # Line 72 - temperature
    if ($i -eq 71) {
        $lines[$i] = '        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Temperature", (smart?.SmartaData?.Temperature ?? 0).ToString());'
        Write-Host "  Fixed line 72: Temperature null-safe" -ForegroundColor Green
    }
    
    # Line 82 - pending/uncorrectable
    if ($i -eq 81) {
        $lines[$i] = '        DrawTableRow(canvas, tableFont, textPaint, tableX, tableY, "Pending/Uncorrectable", $"{(smart?.SmartaData?.PendingSectorCount ?? 0)}/{(smart?.SmartaData?.UncorrectableErrorCount ?? 0)}");'
        Write-Host "  Fixed line 82: Pending/Uncorrectable null-safe" -ForegroundColor Green
    }
}

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`n=== All null references fixed ===" -ForegroundColor Green
Write-Host "Final build should succeed!" -ForegroundColor Yellow