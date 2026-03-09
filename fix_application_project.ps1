# Comprehensive fix for Application Project - 34 errors remaining

Write-Host "=== Fixing Application Project Errors ===" -ForegroundColor Green

# 1. Fix int? ?? string issues (10 errors) - use ToSafeString()
Write-Host "`n1. Fixing int? ?? string issues..." -ForegroundColor Yellow

# SmartCheckService.cs
$file1 = "DiskChecker.Application\Services\SmartCheckService.cs"
$lines1 = [System.IO.File]::ReadAllLines($file1, [System.Text.Encoding]::UTF8)

# Find and fix int? ?? string patterns
for ($i = 0; $i -lt $lines1.Count; $i++) {
    $line = $lines1[$i]
    
    # Pattern: DeviceModel ?? "..." or ModelFamily ?? "..."
    if ($line -match "(\w+)\s*\?\?\s*\"([^\"]+)\"") {
        $varName = $matches[1]
        $defaultValue = $matches[2]
        if ($varName -in @("smartaData.DeviceModel", "smartaData.ModelFamily", "DeviceModel", "ModelFamily")) {
            $lines1[$i] = $line -replace "(\w+)\s*\?\?\s*\"([^\"]+)\"", '$1?.ToSafeString() ?? "$2"'
            Write-Host "  Fixed line $($i+1): $($lines1[$i])" -ForegroundColor Green
        }
    }
}

[System.IO.File]::WriteAllLines($file1, $lines1, [System.Text.Encoding]::UTF8)
Write-Host "  Saved SmartCheckService.cs" -ForegroundColor Green

# TestReportExportService.cs - fix int? ?? string + DriveInfo.Path
$file2 = "DiskChecker.Application\Services\TestReportExportService.cs"
$lines2 = [System.IO.File]::ReadAllLines($file2, [System.Text.Encoding]::UTF8)

for ($i = 0; $i -lt $lines2.Count; $i++) {
    # Fix DriveInfo.Path - this doesn't exist, use Name instead
    if ($lines2[$i] -match "\.Drive\.Path") {
        $lines2[$i] = $lines2[$i] -replace "\.Drive\.Path", ".Drive.Name"
        Write-Host "  Fixed Drive.Path on line $($i+1)" -ForegroundColor Green
    }
    
    # Fix int? ?? string  
    if ($lines2[$i] -match "(\w+)\s*\?\?\s*\"([^\"]+)\"" -and $lines2[$i] -match "DeviceModel|ModelFamily") {
        $lines2[$i] = $lines2[$i] -replace "(\w+)\s*\?\?\s*\"([^\"]+)\"", '$1?.ToSafeString() ?? "$2"'
        Write-Host "  Fixed int??string on line $($i+1)" -ForegroundColor Green
    }
}

[System.IO.File]::WriteAllLines($file2, $lines2, [System.Text.Encoding]::UTF8)
Write-Host "  Saved TestReportExportService.cs" -ForegroundColor Green

# PdfReportExportService.cs
$file3 = "DiskChecker.Application\Services\PdfReportExportService.cs"
$lines3 = [System.IO.File]::ReadAllLines($file3, [System.Text.Encoding]::UTF8)

for ($i = 0; $i -lt $lines3.Count; $i++) {
    if ($lines3[$i] -match "(\w+)\s*\?\?\s*\"([^\"]+)\"" -and $lines3[$i] -match "DeviceModel|Temperature") {
        $lines3[$i] = $lines3[$i] -replace "(\w+)\s*\?\?\s*\"([^\"]+)\"", '$1?.ToSafeString() ?? "$2"'
        Write-Host "  Fixed int??string on line $($i+1)" -ForegroundColor Green
    }
}

[System.IO.File]::WriteAllLines($file3, $lines3, [System.Text.Encoding]::UTF8)
Write-Host "  Saved PdfReportExportService.cs" -ForegroundColor Green

Write-Host "`n=== Initial fixes applied ===" -ForegroundColor Green
Write-Host "Remaining: Type conversions and other issues" -ForegroundColor Yellow