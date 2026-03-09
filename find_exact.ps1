# Find exact matches for error locations

Write-Host "=== Finding Exact Error Locations ===" -ForegroundColor Green

# 1. DiskSurfaceTestExecutor.cs - Lines 137-139
Write-Host "`n1. DiskSurfaceTestExecutor.cs Lines 137-139:" -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\DiskSurfaceTestExecutor.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
for ($i = 136; $i -lt [Math]::Min(140, $lines.Count); $i++) {
    $lineNum = $i + 1
    Write-Host "$lineNum`t$($lines[$i])" -ForegroundColor Cyan
}

# 2. WindowsSmartaProvider.cs - Line 59 area
Write-Host "`n2. WindowsSmartaProvider.cs Lines 58-62:" -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\WindowsSmartaProvider.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
for ($i = 57; $i -lt [Math]::Min(63, $lines.Count); $i++) {
    $lineNum = $i + 1
    Write-Host "$lineNum`t$($lines[$i])" -ForegroundColor Cyan
}

# 3. SequentialFileTestExecutor.cs - Lines 99-103
Write-Host "`n3. SequentialFileTestExecutor.cs Lines 99-103:" -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\SequentialFileTestExecutor.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
for ($i = 98; $i -lt [Math]::Min(104, $lines.Count); $i++) {
    $lineNum = $i + 1
    Write-Host "$lineNum`t$($lines[$i])" -ForegroundColor Cyan
}

# 4. SurfaceTestExecutor.cs - Lines 46-50
Write-Host "`n4. SurfaceTestExecutor.cs Lines 46-50:" -ForegroundColor Yellow
$file = "DiskChecker.Infrastructure\Hardware\SurfaceTestExecutor.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
for ($i = 45; $i -lt [Math]::Min(51, $lines.Count); $i++) {
    $lineNum = $i + 1
    Write-Host "$lineNum`t$($lines[$i])" -ForegroundColor Cyan
}