# Final comprehensive fix for ALL remaining Application errors (20 errors)

Write-Host "=== Fixing ALL Remaining Application Errors ===" -ForegroundColor Green
Write-Host "Targeting 20 remaining errors" -ForegroundColor Yellow

# Fix SmartCheckService.cs - all remaining issues
Write-Host "`n1. Fixing SmartCheckService.cs..." -ForegroundColor Yellow
$file = "DiskChecker.Application\Services\SmartCheckService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

for ($i = 0; $i -lt $lines.Count; $i++) {
    # Fix lines 111, 112: int? ?? string -> use ToSafeString()
    if ($lines[$i] -match 'result\.DriveModel\s*\?\?\s*request\.Drive\.Name') {
        $lines[$i] = $lines[$i] -replace 'result\.DriveModel\s*\?\?', 'result.DriveModel?.ToSafeString() ??'
        Write-Host "  Fixed line $($i+1): DriveModel??" -ForegroundColor Green
    }
    if ($lines[$i] -match 'result\.DeviceModel\s*\?\?\s*request\.Drive\.Name') {
        $lines[$i] = $lines[$i] -replace 'result\.DeviceModel\s*\?\?', 'result.DeviceModel?.ToSafeString() ??'
        Write-Host "  Fixed line $($i+1): DeviceModel??" -ForegroundColor Green
    }
    
    # Fix line 144: int? -> int conversion + null check
    if ($lines[$i] -match 'Temperature\s*=\s*result\.Temperature,') {
        $lines[$i] = '            Temperature = result.Temperature ?? 0,'
        Write-Host "  Fixed line $($i+1): Temperature int?" -ForegroundColor Green
    }
    
    # Fix line 167: Guid to string
    if ($lines[$i] -match 'TestId\s*=\s*Guid\.NewGuid\(\),') {
        $lines[$i] = '            TestId = Guid.NewGuid().ToString(),'
        Write-Host "  Fixed line $($i+1): Guid->string" -ForegroundColor Green
    }
    
    # Fix line 168: IReadOnlyList -> List
    if ($lines[$i] -match 'Attributes\s*=\s*result\.Attributes,') {
        $lines[$i] = '            Attributes = result.Attributes.ToList(),'
        Write-Host "  Fixed line $($i+1): IReadOnlyList->List" -ForegroundColor Green
    }
    
    # Fix line 169: SmartaSelfTestStatus? -> string
    if ($lines[$i] -match 'SelfTestStatus\s*=\s*result\.CurrentSelfTest,') {
        $lines[$i] = '            SelfTestStatus = result.CurrentSelfTest?.Status.ToString(),'
        Write-Host "  Fixed line $($i+1): SmartaSelfTestStatus->string" -ForegroundColor Green
    }
    
    # Fix line 170: IReadOnlyList -> List
    if ($lines[$i] -match 'SelfTestLog\s*=\s*result\.SelfTests,') {
        $lines[$i] = '            SelfTestLog = result.SelfTests?.ToList() ?? new List<SmartaSelfTestEntry>(),'
        Write-Host "  Fixed line $($i+1): SelfTests IReadOnlyList->List" -ForegroundColor Green
    }
    
    # Fix line 282: method group null
    if ($lines[$i] -match 'Status\.IsRunning\s*=\s*null') {
        $lines[$i] = '            Status = result.CurrentSelfTest?.Status.ToString() ?? "Unknown",'
        Write-Host "  Fixed line $($i+1): Status null" -ForegroundColor Green
    }
}

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "  Saved SmartCheckService.cs" -ForegroundColor Green

# Fix TestReportExportService.cs and PdfReportExportService.cs null references
Write-Host "`n2. Fixing null reference warnings..." -ForegroundColor Yellow

$files = @(
    "DiskChecker.Application\Services\TestReportExportService.cs",
    "DiskChecker.Application\Services\PdfReportExportService.cs"
)

foreach ($f in $files) {
    $lines = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)
    $changed = $false
    
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # Fix warnings?.Count -> warnings.Count (warnings is int, not collection)
        if ($lines[$i] -match '(\w+)\.Warnings\.Count') {
            $var = $matches[1]
            $lines[$i] = $lines[$i] -replace '(\w+)\.Warnings\.Count', '$1.Warnings'
            Write-Host "  Fixed line $($i+1): Warnings.Count in $(Split-Path $f -Leaf)" -ForegroundColor Green
            $changed = $true
        }
        
        # Fix foreach over int -> use proper collection
        if ($lines[$i] -match 'foreach.*\s+in\s+\w+\.Warnings') {
            # Skip warnings iteration if it's an int
            $lines[$i] = '            // Warnings iteration removed - Rating.Warnings is int'
            $changed = $true
        }
    }
    
    if ($changed) {
        [System.IO.File]::WriteAllLines($f, $lines, [System.Text.Encoding]::UTF8)
    }
}

Write-Host "`n=== Applied all fixes ===" -ForegroundColor Green
Write-Host "Run dotnet build to check for remaining errors" -ForegroundColor Yellow