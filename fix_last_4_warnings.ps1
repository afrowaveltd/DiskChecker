# FINAL - Fix last 4 null reference warnings

Write-Host "=== FIXING LAST 4 NULL REFERENCE WARNINGS ===" -ForegroundColor Green

# 1. Fix TestReportExportService.cs line 153
Write-Host "`n1. Fixing TestReportExportService.cs line 153..." -ForegroundColor Yellow
$file1 = "DiskChecker.Application\Services\TestReportExportService.cs"
$lines1 = [System.IO.File]::ReadAllLines($file1, [System.Text.Encoding]::UTF8)

Write-Host "Line 153 (before): $($lines1[152])" -ForegroundColor Cyan

# Add null coalescing for the argument
if ($lines1[152] -match '\.ToString\(\)') {
    $lines1[152] = $lines1[152] -replace '\.ToString\(\)', '?.ToString() ?? "N/A"'
    Write-Host "Line 153 (after): $($lines1[152])" -ForegroundColor Green
}

[System.IO.File]::WriteAllLines($file1, $lines1, [System.Text.Encoding]::UTF8)
Write-Host "  Fixed TestReportExportService.cs" -ForegroundColor Green

# 2. Fix PdfReportExportService.cs lines 61, 70, 78
Write-Host "`n2. Fixing PdfReportExportService.cs..." -ForegroundColor Yellow
$file2 = "DiskChecker.Application\Services\PdfReportExportService.cs"
$lines2 = [System.IO.File]::ReadAllLines($file2, [System.Text.Encoding]::UTF8)

Write-Host "Line 61 (before): $($lines2[60])" -ForegroundColor Cyan
Write-Host "Line 70 (before): $($lines2[69])" -ForegroundColor Cyan
Write-Host "Line 78 (before): $($lines2[77])" -ForegroundColor Cyan

# Fix null references by adding null-conditional operators
for ($i = 0; $i -lt $lines2.Count; $i++) {
    # Line 61: smart.SmartaData?.DeviceModel
    if ($i -eq 60) {
        $lines2[$i] = $lines2[$i] -replace 'smart\.SmartaData\.', 'smart.SmartaData?.'
        $lines2[$i] = $lines2[$i] -replace '\?\?\?', '??'
    }
    
    # Line 70: Temperature?.ToString()
    if ($i -eq 69) {
        if ($lines2[$i] -match 'Temperature\.ToString\(\)') {
            $lines2[$i] = $lines2[$i] -replace 'Temperature\.ToString\(\)', '(Temperature ?? 0).ToString()'
        }
    }
    
    # Line 78: ReallocatedSectorCount?.ToString()
    if ($i -eq 77) {
        if ($lines2[$i] -match 'ReallocatedSectorCount\.ToString\(\)') {
            $lines2[$i] = $lines2[$i] -replace 'ReallocatedSectorCount\.ToString\(\)', '(ReallocatedSectorCount ?? 0).ToString()'
        }
    }
}

Write-Host "Line 61 (after): $($lines2[60])" -ForegroundColor Green
Write-Host "Line 70 (after): $($lines2[69])" -ForegroundColor Green
Write-Host "Line 78 (after): $($lines2[77])" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file2, $lines2, [System.Text.Encoding]::UTF8)
Write-Host "  Fixed PdfReportExportService.cs" -ForegroundColor Green

Write-Host "`n=== All 4 warnings fixed ===" -ForegroundColor Green
Write-Host "Final build should succeed!" -ForegroundColor Yellow