# Comprehensive fix for Application Project - 34 errors remaining

Write-Host "=== Fixing Application Project Errors ===" -ForegroundColor Green

# 1. Fix int? ?? string issues (10 errors) - use ToSafeString()
Write-Host "`n1. Fixing int? ?? string issues..." -ForegroundColor Yellow

# SmartCheckService.cs
$file1 = "DiskChecker.Application\Services\SmartCheckService.cs"
$lines1 = [System.IO.File]::ReadAllLines($file1, [System.Text.Encoding]::UTF8)

$fixed1 = 0
for ($i = 0; $i -lt $lines1.Count; $i++) {
    $original = $lines1[$i]
    
    # Fix: deviceModel ?? "..." => deviceModel?.ToSafeString() ?? "..."
    if ($lines1[$i] -match 'smartaData\.DeviceModel\s*\?\?\s*\"') {
        $lines1[$i] = $lines1[$i] -replace 'smartaData\.DeviceModel\s*\?\?', 'smartaData.DeviceModel?.ToSafeString() ??'
        $fixed1++
    }
    if ($lines1[$i] -match 'smartaData\.ModelFamily\s*\?\?\s*\"') {
        $lines1[$i] = $lines1[$i] -replace 'smartaData\.ModelFamily\s*\?\?', 'smartaData.ModelFamily?.ToSafeString() ??'
        $fixed1++
    }
    if ($lines1[$i] -match 'DeviceModel\s*\?\?\s*drive\.Name') {
        $lines1[$i] = $lines1[$i] -replace 'DeviceModel\s*\?\?', 'DeviceModel?.ToSafeString() ??'
        $fixed1++
    }
}

[System.IO.File]::WriteAllLines($file1, $lines1, [System.Text.Encoding]::UTF8)
Write-Host "  Fixed $fixed1 issues in SmartCheckService.cs" -ForegroundColor Green

# TestReportExportService.cs & PdfReportExportService.cs
$files = @(
    "DiskChecker.Application\Services\TestReportExportService.cs",
    "DiskChecker.Application\Services\PdfReportExportService.cs"
)

foreach ($file in $files) {
    $lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
    $fixed = 0
    
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # Fix DriveInfo.Path - doesn't exist
        if ($lines[$i] -match '\.Drive\.Path\b') {
            $lines[$i] = $lines[$i] -replace '\.Drive\.Path\b', '.Drive.Name'
            $fixed++
        }
        
        # Fix int? ?? string
        if ($lines[$i] -match 'result\.(?:DeviceModel|ModelFamily|Temperature)\s*\?\?\s*\"') {
            $lines[$i] = $lines[$i] -replace 'result\.(DeviceModel|ModelFamily|Temperature)\s*\?\?', 'result.$1?.ToSafeString() ??'
            $fixed++
        }
    }
    
    [System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "  Fixed $fixed issues in $(Split-Path $file -Leaf)" -ForegroundColor Green
}

Write-Host "`n=== Applied initial fixes ===" -ForegroundColor Green
Write-Host "Run dotnet build to see remaining errors" -ForegroundColor Yellow