# Apply compilation fixes to Infrastructure Project files
# Total: 19 errors to fix

Write-Host "=== Applying Fixes to Infrastructure Project ===" -ForegroundColor Green

# 1. Fix DiskSurfaceTestExecutor.cs - Lines 137-139 (3 errors)
Write-Host "`n1. Fixing DiskSurfaceTestExecutor.cs..." -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\DiskSurfaceTestExecutor.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

# Line 138: Remove ToSafeString from SerialNumber (it's already string)
$lines[137] = '                result.DriveSerialNumber = smartData.SerialNumber ?? string.Empty;'
Write-Host "  Fixed line 138: Removed ToSafeString from SerialNumber" -ForegroundColor Cyan

# Line 139: Add ToSafeString for DeviceModel parameter
$lines[138] = '                result.DriveManufacturer = ExtractManufacturer(smartData.DeviceModel?.ToSafeString());'
Write-Host "  Fixed line 139: Added ToSafeString for DeviceModel parameter" -ForegroundColor Cyan

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "  Saved DiskSurfaceTestExecutor.cs" -ForegroundColor Green

# 2. Fix SequentialFileTestExecutor.cs - Lines 100,102 (2 errors)
Write-Host "`n2. Fixing SequentialFileTestExecutor.cs..." -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\SequentialFileTestExecutor.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

# Line 100: Fix ?? operator with int?
$lines[99] = '               result.DriveModel = smartData.DeviceModel?.ToSafeString() ?? request.Drive.Name;'
Write-Host "  Fixed line 100: Added ToSafeString for DeviceModel" -ForegroundColor Cyan

# Line 102: Add ToSafeString for DeviceModel parameter
$lines[101] = '               result.DriveManufacturer = ExtractManufacturer(smartData.DeviceModel?.ToSafeString());'
Write-Host "  Fixed line 102: Added ToSafeString for DeviceModel parameter" -ForegroundColor Cyan

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "  Saved SequentialFileTestExecutor.cs" -ForegroundColor Green

# 3. Fix SurfaceTestExecutor.cs - Lines 47,49 (2 errors)
Write-Host "`n3. Fixing SurfaceTestExecutor.cs..." -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\SurfaceTestExecutor.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

# Line 47: DeviceModel is int? but being assigned to string
$lines[46] = '                result.DriveModel = string.IsNullOrWhiteSpace(meta.DeviceModel?.ToSafeString()) ? request.Drive.Name : meta.DeviceModel?.ToSafeString();'
Write-Host "  Fixed line 47: Added ToSafeString for DeviceModel" -ForegroundColor Cyan

# Line 49: ModelFamily is int? but being assigned to string
$lines[48] = '                result.DriveManufacturer = string.IsNullOrWhiteSpace(meta.ModelFamily?.ToSafeString()) ? null : meta.ModelFamily?.ToSafeString();'
Write-Host "  Fixed line 49: Added ToSafeString for ModelFamily" -ForegroundColor Cyan

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "  Saved SurfaceTestExecutor.cs" -ForegroundColor Green

Write-Host "`n=== Applied 7 fixes ===" -ForegroundColor Green
Write-Host "Remaining: 12 errors in WindowsSmartaProvider.cs and SmartctlJsonParser.cs" -ForegroundColor Yellow