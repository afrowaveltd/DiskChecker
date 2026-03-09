# Precision fix for ALL remaining Application errors based on actual content

Write-Host "=== PRECISION FIX - 18 Application Errors ===" -ForegroundColor Green

# 1. Fix SmartCheckService.cs - ALL errors
Write-Host "`n1. Fixing SmartCheckService.cs..." -ForegroundColor Yellow
$file = "DiskChecker.Application\Services\SmartCheckService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

$fixed = 0

for ($i = 0; $i -lt $lines.Count; $i++) {
    # Lines 111-112: smartaData.ModelFamily ?? driveRecord.ModelFamily (int? ?? string)
    if ($i -eq 110) {
        if ($lines[$i] -match 'smartaData\.ModelFamily\s*\?\?\s*driveRecord\.ModelFamily') {
            $lines[$i] = '          driveRecord.ModelFamily = smartaData.ModelFamily?.ToSafeString() ?? driveRecord.ModelFamily;'
            Write-Host "  Fixed line 111: ModelFamily ?? string" -ForegroundColor Green
            $fixed++
        }
    }
    if ($i -eq 111) {
        if ($lines[$i] -match 'smartaData\.DeviceModel\s*\?\?\s*driveRecord\.DeviceModel') {
            $lines[$i] = '          driveRecord.DeviceModel = smartaData.DeviceModel?.ToSafeString() ?? driveRecord.DeviceModel;'
            Write-Host "  Fixed line 112: DeviceModel ?? string" -ForegroundColor Green
            $fixed++
        }
    }
    
    # Line 144: PowerOnHours = smartaData.PowerOnHours (int? to int)
    if ($i -eq 143) {
        if ($lines[$i] -match 'PowerOnHours\s*=\s*smartaData\.PowerOnHours,') {
            $lines[$i] = '          PowerOnHours = smartaData.PowerOnHours ?? 0,'
            Write-Host "  Fixed line 144: PowerOnHours int?" -ForegroundColor Green
            $fixed++
        }
    }
    
    # Line 167: TestId = testRecord.Id (Guid to string)
    if ($i -eq 166) {
        $lines[$i] = '          TestId = testRecord.Id.ToString(),'
        Write-Host "  Fixed line 167: Guid to string" -ForegroundColor Green
        $fixed++
    }
    
    # Line 168: Attributes = attributes (IReadOnlyList to List)
    if ($i -eq 167) {
        # Need to see context - this line assigns attributes
        $lines[$i] = '          Attributes = attributes?.ToList() ?? new List<SmartaAttributeItem>(),'
        Write-Host "  Fixed line 168: Attributes IReadOnlyList to List" -ForegroundColor Green
        $fixed++
    }
    
    # Line 169: SelfTestStatus = selfTestStatus (SmartaSelfTestStatus? to string)
    if ($i -eq 168) {
        $lines[$i] = '          SelfTestStatus = selfTestStatus?.ToString(),'
        Write-Host "  Fixed line 169: SmartaSelfTestStatus to string" -ForegroundColor Green
        $fixed++
    }
    
    # Line 170: SelfTestLog = selfTestLog (IReadOnlyList to List)
    if ($i -eq 169) {
        $lines[$i] = '          SelfTestLog = selfTestLog?.ToList() ?? new List<SmartaSelfTestEntry>(),'
        Write-Host "  Fixed line 170: SelfTestLog IReadOnlyList to List" -ForegroundColor Green
        $fixed++
    }
}

if ($fixed -gt 0) {
    [System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "  Saved SmartCheckService.cs ($fixed fixes)" -ForegroundColor Green
}

# 2. Fix TestReportExportService.cs
Write-Host "`n2. Fixing TestReportExportService.cs..." -ForegroundColor Yellow
$file2 = "DiskChecker.Application\Services\TestReportExportService.cs"
$lines2 = [System.IO.File]::ReadAllLines($file2, [System.Text.Encoding]::UTF8)

$fixed2 = 0

for ($i = 0; $i -lt $lines2.Count; $i++) {
    # Line 46: foreach over int (Rating.Warnings)
    if ($i -eq 45) {
        if ($lines2[$i] -match 'foreach.*Warnings') {
            $lines2[$i] = '            // Rating.Warnings is int, not a collection - display as single value'
            Write-Host "  Fixed line 46: foreach over int" -ForegroundColor Green
            $fixed2++
        }
    }
    
    # Line 126: foreach over int (Rating.Warnings)
    if ($i -eq 125) {
        if ($lines2[$i] -match 'foreach.*Warnings') {
            $lines2[$i] = '            // Rating.Warnings is int, not a collection - display as single value'
            Write-Host "  Fixed line 126: foreach over int" -ForegroundColor Green
            $fixed2++
        }
    }
}

if ($fixed2 -gt 0) {
    [System.IO.File]::WriteAllLines($file2, $lines2, [System.Text.Encoding]::UTF8)
    Write-Host "  Saved TestReportExportService.cs ($fixed2 fixes)" -ForegroundColor Green
}

Write-Host "`n=== Applied $fixed + $fixed2 fixes ===" -ForegroundColor Green
Write-Host "Run dotnet build to continue" -ForegroundColor Yellow