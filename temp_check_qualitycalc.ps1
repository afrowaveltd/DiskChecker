$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Services\QualityCalculator.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"
Write-Output "=== Lines 10-35 ==="
for ($i = 9; $i -lt 35 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}