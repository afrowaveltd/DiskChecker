# Fix Infrastructure Project Compilation Errors
# Total: 19 errors remaining

Write-Host "=== Fixing Infrastructure Project Errors ===" -ForegroundColor Green

# 1. DiskSurfaceTestExecutor.cs - Lines 137-139
Write-Host "`n1. DiskSurfaceTestExecutor.cs (3 errors)" -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\DiskSurfaceTestExecutor.cs"
$content = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Line 137: $($content[136])"
Write-Host "Line 138: $($content[137])"
Write-Host "Line 139: $($content[138])"

# 2. WindowsSmartaProvider.cs - Lines 59,60,77,85,495,507,512
Write-Host "`n2. WindowsSmartaProvider.cs (7 errors)" -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\WindowsSmartaProvider.cs"
$content = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Line 59: $($content[58])"
Write-Host "Line 60: $($content[59])"
Write-Host "Line 77: $($content[76])"
Write-Host "Line 85: $($content[84])"
Write-Host "Line 495: $($content[494])"
Write-Host "Line 507: $($content[506])"
Write-Host "Line 512: $($content[511])"

# 3. SmartctlJsonParser.cs - Lines 140,149,254,255
Write-Host "`n3. SmartctlJsonParser.cs (4 errors)" -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs"
$content = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Line 140: $($content[139])"
Write-Host "Line 149: $($content[148])"
Write-Host "Line 254: $($content[253])"
Write-Host "Line 255: $($content[254])"

# 4. SequentialFileTestExecutor.cs - Lines 100,102
Write-Host "`n4. SequentialFileTestExecutor.cs (2 errors)" -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\SequentialFileTestExecutor.cs"
$content = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Line 100: $($content[99])"
Write-Host "Line 102: $($content[101])"

# 5. SurfaceTestExecutor.cs - Lines 47,49
Write-Host "`n5. SurfaceTestExecutor.cs (2 errors)" -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\SurfaceTestExecutor.cs"
$content = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Line 47: $($content[46])"
Write-Host "Line 49: $($content[48])"

Write-Host "`n=== All problematic lines identified ===" -ForegroundColor Green