# Fix CS8601 null reference warning

Write-Host "=== Fixing CS8601 Warning ===" -ForegroundColor Green

$file = "DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "`nLine 149: $($lines[148])" -ForegroundColor Cyan
Write-Host "Line 150: $($lines[149])" -ForegroundColor Cyan
Write-Host "Line 151: $($lines[150])" -ForegroundColor Cyan

# Problem: returning null for non-nullable string WhenFailed
# Solution: Change ': null' to ': string.Empty'

# Fix line 149-151
$lines[148] = '                    WhenFailed = item.TryGetProperty("when_failed", out var wf) && wf.ValueKind != JsonValueKind.Null '
$lines[149] = '                        ? wf.GetString() ?? string.Empty '
$lines[150] = '                        : string.Empty'

Write-Host "`nFixed lines 149-151: Changed null to string.Empty for WhenFailed" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`n=== Fixed CS8601 warning ===" -ForegroundColor Green