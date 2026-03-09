# Apply fixes to WindowsSmartaProvider.cs - 7 errors

Write-Host "=== Fixing WindowsSmartaProvider.cs ===" -ForegroundColor Green

$file = "DiskChecker.Infrastructure\Hardware\WindowsSmartaProvider.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "`nCurrent problematic lines:" -ForegroundColor Yellow

# Line 59: smartctl.DeviceModel.Contains() - DeviceModel is int?
Write-Host "Line 59: $($lines[58])" -ForegroundColor Cyan
$lines[58] = '            if ((string.IsNullOrEmpty(smartctl.DeviceModel?.ToSafeString()) || smartctl.DeviceModel?.ToSafeString()?.Contains("USB", StringComparison.OrdinalIgnoreCase) == true) '
Write-Host "  Fixed line 59: Added ToSafeString for DeviceModel.Contains()" -ForegroundColor Green

# Line 60: windows.DeviceModel - DeviceModel is int?
Write-Host "Line 60: $($lines[59])" -ForegroundColor Cyan
$lines[59] = '                && !string.IsNullOrEmpty(windows.DeviceModel?.ToSafeString()))'
Write-Host "  Fixed line 60: Added ToSafeString for DeviceModel" -ForegroundColor Green

# Line 77: result.DeviceModel - DeviceModel is int?
Write-Host "Line 77: $($lines[76])" -ForegroundColor Cyan
$lines[76] = '            if (string.IsNullOrEmpty(result.DeviceModel?.ToSafeString()))'
Write-Host "  Fixed line 77: Added ToSafeString for DeviceModel" -ForegroundColor Green

# Line 85: result.ModelFamily - ModelFamily is int?
Write-Host "Line 85: $($lines[84])" -ForegroundColor Cyan
$lines[84] = '            if (string.IsNullOrEmpty(result.ModelFamily?.ToSafeString()))'
Write-Host "  Fixed line 85: Added ToSafeString for ModelFamily" -ForegroundColor Green

# Line 495: data.DeviceModel - DeviceModel is int?
Write-Host "Line 495: $($lines[494])" -ForegroundColor Cyan
$lines[494] = '        if (!string.IsNullOrEmpty(data.DeviceModel?.ToSafeString())) score += 40;'
Write-Host "  Fixed line 495: Added ToSafeString for DeviceModel" -ForegroundColor Green

# Line 507: data.ModelFamily - ModelFamily is int?
Write-Host "Line 507: $($lines[506])" -ForegroundColor Cyan
$lines[506] = '        if (!string.IsNullOrEmpty(data.ModelFamily?.ToSafeString())) score += 10;'
Write-Host "  Fixed line 507: Added ToSafeString for ModelFamily" -ForegroundColor Green

# Line 512: d.DeviceModel - DeviceModel is int?
Write-Host "Line 512: $($lines[511])" -ForegroundColor Cyan
$lines[511] = '    private bool IsDataEmpty(SmartaData? d) => d == null || (d.Temperature <= 0 && d.PowerOnHours <= 0 && string.IsNullOrEmpty(d.DeviceModel?.ToSafeString()));'
Write-Host "  Fixed line 512: Added ToSafeString for DeviceModel" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`n=== Applied 7 fixes to WindowsSmartaProvider.cs ===" -ForegroundColor Green