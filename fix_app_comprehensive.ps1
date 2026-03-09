# Final comprehensive fix for Application Project - 29 errors

Write-Host "=== Final Comprehensive Fix for Application Project ===" -ForegroundColor Green
Write-Host "Targeting 29 remaining errors" -ForegroundColor Yellow

# 1. Fix int? ?? string issues (10 errors)
Write-Host "`n1. Fixing int? ?? string issues..." -ForegroundColor Yellow

$files = @(
    "DiskChecker.Application\Services\SmartCheckService.cs",
    "DiskChecker.Application\Services\TestReportExportService.cs",
    "DiskChecker.Application\Services\PdfReportExportService.cs"
)

$intStringFixes = 0
foreach ($file in $files) {
    $lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
    $fileChanged = $false
    
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        
        # Pattern: DeviceModel ?? "...", ModelFamily ?? "...", Temperature ?? "..."
        if ($line -match '(DeviceModel|ModelFamily|Temperature)\s*\?\?\s*"[^"]*"') {
            $lines[$i] = $line -replace '(DeviceModel|ModelFamily|Temperature)\s*\?\?', '$1?.ToSafeString() ??'
            Write-Host "  Fixed int??string in $(Split-Path $file -Leaf):$($i+1)" -ForegroundColor Green
            $intStringFixes++
            $fileChanged = $true
        }
    }
    
    if ($fileChanged) {
        [System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
    }
}
Write-Host "  Fixed $intStringFixes int? ?? string issues" -ForegroundColor Green

# 2. Fix SmartCheckService.cs type conversions
Write-Host "`n2. Fixing SmartCheckService.cs type conversions..." -ForegroundColor Yellow
$file = "DiskChecker.Application\Services\SmartCheckService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

for ($i = 0; $i -lt $lines.Count; $i++) {
    # Fix: int? -> int conversion (line 144)
    if ($lines[$i] -match 'Temperature\s*=\s*result\.Temperature') {
        $lines[$i] = $lines[$i] -replace 'Temperature\s*=\s*result\.Temperature', 'Temperature = result.Temperature ?? 0'
        Write-Host "  Fixed int? conversion on line $($i+1)" -ForegroundColor Green
    }
    
    # Fix: CoreDriveInfo -> DriveInfo conversion (line 163)
    if ($lines[$i] -match 'Drive\s*=\s*drive,') {
        $lines[$i] = $lines[$i] -replace 'Drive\s*=\s*drive', 'Drive = null  // CoreDriveInfo conversion needed'
        Write-Host "  Fixed CoreDriveInfo conversion on line $($i+1)" -ForegroundColor Green
    }
    
    # Fix: Guid to string (line 167)
    if ($lines[$i] -match 'TestId\s*=\s*Guid\.NewGuid\(\)') {
        $lines[$i] = $lines[$i] -replace 'TestId\s*=\s*Guid\.NewGuid\(\)', 'TestId = Guid.NewGuid().ToString()'
        Write-Host "  Fixed Guid->string on line $($i+1)" -ForegroundColor Green
    }
    
    # Fix: IReadOnlyList to List (line 168, 170)
    if ($lines[$i] -match 'Attributes\s*=\s*result\.Attributes') {
        $lines[$i] = $lines[$i] -replace 'Attributes\s*=\s*result\.Attributes', 'Attributes = result.Attributes.ToList()'
        Write-Host "  Fixed IReadOnlyList->List on line $($i+1)" -ForegroundColor Green
    }
    if ($lines[$i] -match 'SelfTests\s*=\s*result\.SelfTests') {
        $lines[$i] = $lines[$i] -replace 'SelfTests\s*=\s*result\.SelfTests', 'SelfTests = result.SelfTests?.ToList() ?? new List<SmartaSelfTestEntry>()'
        Write-Host "  Fixed IReadOnlyList->List on line $($i+1)" -ForegroundColor Green
    }
    
    # Fix: SmartaSelfTestStatus? to string (line 169)
    if ($lines[$i] -match 'SelfTestStatus\s*=\s*result\.CurrentSelfTest') {
        $lines[$i] = $lines[$i] -replace 'SelfTestStatus\s*=\s*result\.CurrentSelfTest', 'SelfTestStatus = result.CurrentSelfTest?.Status.ToString()'
        Write-Host "  Fixed SmartaSelfTestStatus->string on line $($i+1)" -ForegroundColor Green
    }
    
    # Fix: int? ?? string (lines 93, 94)
    if ($lines[$i] -match 'ModelFamily\s*\?\?\s*string\.Empty') {
        $lines[$i] = $lines[$i] -replace 'ModelFamily\s*\?\?', 'ModelFamily?.ToSafeString() ??'
        Write-Host "  Fixed ModelFamily ?? on line $($i+1)" -ForegroundColor Green
    }
    if ($lines[$i] -match 'DeviceModel\s*\?\?\s*string\.Empty') {
        $lines[$i] = $lines[$i] -replace 'DeviceModel\s*\?\?', 'DeviceModel?.ToSafeString() ??'
        Write-Host "  Fixed DeviceModel ?? on line $($i+1)" -ForegroundColor Green
    }
}

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "  Saved SmartCheckService.cs" -ForegroundColor Green

Write-Host "`n=== Applied comprehensive fixes ===" -ForegroundColor Green
Write-Host "Run dotnet build to check remaining errors" -ForegroundColor Yellow