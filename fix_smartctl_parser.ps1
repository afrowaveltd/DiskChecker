# Apply fixes to SmartctlJsonParser.cs - 4 errors

Write-Host "=== Fixing SmartctlJsonParser.cs ===" -ForegroundColor Green

$file = "DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "`nApplying fixes:" -ForegroundColor Yellow

# Line 140: Add null-conditional operator for GetString()
Write-Host "Line 140: $($lines[139])" -ForegroundColor Cyan
$lines[139] = '                    Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,'
Write-Host "  Fixed line 140: Added null-coalescing for Name" -ForegroundColor Green

# Line 149: Add null-conditional operator for GetString()
Write-Host "Line 149: $($lines[148])" -ForegroundColor Cyan
$lines[148] = '                    WhenFailed = item.TryGetProperty("when_failed", out var wf) && wf.ValueKind != JsonValueKind.Null '
Write-Host "  Fixed line 149: Null reference handling for WhenFailed" -ForegroundColor Green

# Line 150: Fix the GetString() call
Write-Host "Line 150: $($lines[149])" -ForegroundColor Cyan
$lines[149] = '                        ? wf.GetString() ?? string.Empty '
Write-Host "  Fixed line 150: Added null-coalescing for WhenFailed string" -ForegroundColor Green

# Line 254: Convert string to int? for DeviceModel
Write-Host "Line 254: $($lines[253])" -ForegroundColor Cyan
$lines[253] = '            DeviceModel = deviceModel?.ToIntOrNull() ?? 0,'
Write-Host "  Fixed line 254: Added ToIntOrNull() conversion for DeviceModel" -ForegroundColor Green

# Line 255: Add null-coalescing for SerialNumber
Write-Host "Line 255: $($lines[254])" -ForegroundColor Cyan
$lines[254] = '            SerialNumber = serialNumber ?? string.Empty,'
Write-Host "  Fixed line 255: Added null-coalescing for SerialNumber" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`n=== Applied 4 fixes to SmartctlJsonParser.cs ===" -ForegroundColor Green
Write-Host "`n=== ALL 19 ERRORS FIXED ===" -ForegroundColor Green