$encoding = [System.Text.Encoding]::UTF8
$path = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = [System.IO.File]::ReadAllLines($path, $encoding)

Write-Output "=== FINAL VERIFICATION ==="

# 1. Check for ROW_NUMBER in non-comment SQL code
Write-Output ""
Write-Output "1. ROW_NUMBER in non-comment code:"
$found = $false
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'ROW_NUMBER' -and $lines[$i] -notmatch '^\s*//') {
        $found = $true
        Write-Output "   LINE $($i+1): $($lines[$i].Trim())"
    }
}
if (-not $found) { Write-Output "   (none - OK)" }

# 2. Check for ConfigureSqlite calls that are NOT commented out
Write-Output ""
Write-Output "2. Active ConfigureSqliteReadOptimizationsAsync calls:"
$found = $false
for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    if ($line -match 'ConfigureSqlite' -and $line -notmatch '^\s*//' -and $line -notmatch 'private\s+static\s+async\s+Task') {
        $found = $true
        Write-Output "   LINE $($i+1): $($line.Trim())"
    }
}
if (-not $found) { Write-Output "   (none - OK)" }

# 3. Check for old Czech comments
Write-Output ""
Write-Output "3. Czech text remaining in sampling-related comments:"
$found = $false
for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    if ($line -match '^\s*///.*[ěščřžýáíéúůŘŠČŘŽÝÁÍÉÚŮ]') {
        $found = $true
        $ctx = ''
        if ($i -gt 0) { $ctx += "$($i):$($lines[$i-1].Trim().Substring(0, [Math]::Min(40, $lines[$i-1].Trim().Length)))" }
        Write-Output "   LINE $($i+1): $($line.Trim())"
    }
}
if (-not $found) { Write-Output "   (none - OK)" }

# 4. Check method signatures around the fixed areas
Write-Output ""
Write-Output "4. Method signatures near fixes:"
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'GetSpeedSampleSeriesDownsampledAsync|GetTemperatureSampleSeriesDownsampledAsync|LoadSpeedSeriesDownsampledAsync') {
        Write-Output "   LINE $($i+1): $($lines[$i].Trim())"
    }
}

Write-Output ""
Write-Output "=== DONE ==="
