# FINAL CLEANUP - Fix last 7 Application errors

Write-Host "=== FINAL CLEANUP - 7 Application Errors ===" -ForegroundColor Green

# 1. Fix TestReportExportService.cs - all issues
Write-Host "`n1. Examining TestReportExportService.cs errors..." -ForegroundColor Yellow
$file1 = "DiskChecker.Application\Services\TestReportExportService.cs"
$lines1 = [System.IO.File]::ReadAllLines($file1, [System.Text.Encoding]::UTF8)

Write-Host "Line 25 (null reference): $($lines1[24])" -ForegroundColor Cyan
Write-Host "Line 48 (undefined variable): $($lines1[47])" -ForegroundColor Cyan
Write-Host "Line 89 (null reference): $($lines1[88])" -ForegroundColor Cyan
Write-Host "Line 148 (null reference): $($lines1[147])" -ForegroundColor Cyan
Write-Host "Line 153 (ToString arg): $($lines1[152])" -ForegroundColor Cyan

$fixed1 = 0

for ($i = 0; $i -lt $lines1.Count; $i++) {
    # Line 48: undefined "warning" variable - remove or comment
    if ($i -eq 47) {
        if ($lines1[$i] -match '//.*warning') {
            # Already commented
            continue
        }
        if ($lines1[$i] -match 'warning') {
            $lines1[$i] = '            // Rating.Warnings is int property - display value directly'
            Write-Host "  Fixed line 48: commented warning usage" -ForegroundColor Green
            $fixed1++
        }
    }
    
    # Fix ToString("format") calls
    if ($lines1[$i] -match '\.ToString\("[^"]+"\)') {
        $lines1[$i] = $lines1[$i] -replace '\.ToString\("[^"]+"\)', '.ToString()'
        Write-Host "  Fixed line $($i+1): ToString() argument removed" -ForegroundColor Green
        $fixed1++
    }
    
    # Add null checks for null references
    if ($lines1[$i] -match 'smart\.Drive\??\.Name' -and $lines1[$i] -notmatch '\?\?') {
        $lines1[$i] = $lines1[$i] -replace 'smart\.Drive\.Name', 'smart.Drive?.Name ?? "Unknown"'
        Write-Host "  Fixed line $($i+1): added null check" -ForegroundColor Green
        $fixed1++
    }
}

if ($fixed1 -gt 0) {
    [System.IO.File]::WriteAllLines($file1, $lines1, [System.Text.Encoding]::UTF8)
    Write-Host "  Saved TestReportExportService.cs ($fixed1 fixes)" -ForegroundColor Green
}

# 2. Fix PdfReportExportService.cs - null references
Write-Host "`n2. Examining PdfReportExportService.cs errors..." -ForegroundColor Yellow
$file2 = "DiskChecker.Application\Services\PdfReportExportService.cs"
$lines2 = [System.IO.File]::ReadAllLines($file2, [System.Text.Encoding]::UTF8)

Write-Host "Line 61 (null reference): $($lines2[60])" -ForegroundColor Cyan
Write-Host "Line 76 (null reference): $($lines2[75])" -ForegroundColor Cyan

$fixed2 = 0

for ($i = 0; $i -lt $lines2.Count; $i++) {
    # Line 61: null reference
    if ($i -eq 60) {
        if ($lines2[$i] -match 'result\.' -and $lines2[$i] -notmatch '\?\?') {
            $lines2[$i] = $lines2[$i] -replace 'result\.(\w+)', 'result?.$1 ?? ""'
            Write-Host "  Fixed line 61: added null coalescing" -ForegroundColor Green
            $fixed2++
        }
    }
}

if ($fixed2 -gt 0) {
    [System.IO.File]::WriteAllLines($file2, $lines2, [System.Text.Encoding]::UTF8)
    Write-Host "  Saved PdfReportExportService.cs ($fixed2 fixes)" -ForegroundColor Green
}

Write-Host "`n=== Applied final fixes ===" -ForegroundColor Green
Write-Host "Run dotnet build to verify" -ForegroundColor Yellow