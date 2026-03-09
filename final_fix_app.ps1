# FINAL comprehensive fix for ALL 18 remaining Application errors

Write-Host "=== FINAL FIX - 18 Remaining Application Errors ===" -ForegroundColor Green

# 1. Fix SmartCheckService.cs - Check actual line content first
Write-Host "`n1. Examining SmartCheckService.cs errors..." -ForegroundColor Yellow
$file = "DiskChecker.Application\Services\SmartCheckService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Lines 111-112 (int? ?? string):" -ForegroundColor Cyan
Write-Host "  111: $($lines[110])"
Write-Host "  112: $($lines[111])"

Write-Host "`nLines 144, 167-170 (conversions):" -ForegroundColor Cyan
Write-Host "  144: $($lines[143])"
Write-Host "  167: $($lines[166])"
Write-Host "  168: $($lines[167])"
Write-Host "  169: $($lines[168])"
Write-Host "  170: $($lines[169])"

Write-Host "`nLine 282 (method group null):" -ForegroundColor Cyan
Write-Host "  282: $($lines[281])"

# Apply fixes based on actual content
$fixed = 0

for ($i = 0; $i -lt $lines.Count; $i++) {
    # Fix lines 111, 112: result.DeviceModel ?? "..." or result.DriveModel ?? "..."
    if ($i -eq 110 -or $i -eq 111) {
        if ($lines[$i] -match '(DeviceModel|DriveModel)\s*\?\?\s*"[^"]*"') {
            $lines[$i] = $lines[$i] -replace '(DeviceModel|DriveModel)\s*\?\?', '$1?.ToSafeString() ??'
            Write-Host "  Fixed line $($i+1): Added ToSafeString()" -ForegroundColor Green
            $fixed++
        }
    }
    
    # Fix line 144: Temperature = result.Temperature (int? to int)
    if ($i -eq 143) {
        if ($lines[$i] -match 'Temperature\s*=\s*result\.Temperature,') {
            $lines[$i] = '            Temperature = result.Temperature ?? 0,'
            Write-Host "  Fixed line $($i+1): Temperature int?" -ForegroundColor Green
            $fixed++
        }
    }
    
    # Fix line 167: TestId = Guid.NewGuid() (Guid to string)
    if ($i -eq 166) {
        if ($lines[$i] -match 'TestId\s*=\s*Guid\.NewGuid\(\),') {
            $lines[$i] = '            TestId = Guid.NewGuid().ToString(),'
            Write-Host "  Fixed line $($i+1): Guid to string" -ForegroundColor Green
            $fixed++
        }
    }
    
    # Fix line 168: Attributes = result.Attributes (IReadOnlyList to List)
    if ($i -eq 167) {
        if ($lines[$i] -match 'Attributes\s*=\s*result\.Attributes,') {
            $lines[$i] = '            Attributes = result.Attributes.ToList(),'
            Write-Host "  Fixed line $($i+1): IReadOnlyList to List" -ForegroundColor Green
            $fixed++
        }
    }
    
    # Fix line 169: SelfTestStatus = result.CurrentSelfTest (SmartaSelfTestStatus? to string)
    if ($i -eq 168) {
        if ($lines[$i] -match 'SelfTestStatus\s*=\s*result\.CurrentSelfTest,') {
            $lines[$i] = '            SelfTestStatus = result.CurrentSelfTest?.Status.ToString(),'
            Write-Host "  Fixed line $($i+1): SmartaSelfTestStatus to string" -ForegroundColor Green
            $fixed++
        }
    }
    
    # Fix line 170: SelfTestLog = result.SelfTests (IReadOnlyList to List)
    if ($i -eq 169) {
        if ($lines[$i] -match 'SelfTestLog\s*=\s*result\.SelfTests,') {
            $lines[$i] = '            SelfTestLog = result.SelfTests?.ToList() ?? new List<SmartaSelfTestEntry>(),'
            Write-Host "  Fixed line $($i+1): SelfTests IReadOnlyList to List" -ForegroundColor Green
            $fixed++
        }
    }
}

if ($fixed -gt 0) {
    [System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
    Write-Host "  Saved SmartCheckService.cs ($fixed fixes)" -ForegroundColor Green
}

Write-Host "`nApplied $fixed fixes to SmartCheckService.cs" -ForegroundColor Green
Write-Host "`nRun dotnet build to check remaining errors" -ForegroundColor Yellow