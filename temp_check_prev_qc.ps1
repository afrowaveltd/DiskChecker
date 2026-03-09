$utf8 = New-Object System.Text.UTF8Encoding $false
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Services\QualityCalculator.cs"
$prevContent = [System.IO.File]::ReadAllText($prevPath, $utf8)
$prevLines = $prevContent -split "`n"

Write-Output "=== QualityCalculator in Previous ==="
for ($i = 0; $i -lt 40 -and $i -lt $prevLines.Count; $i++) {
    Write-Output "$($i+1): $($prevLines[$i])"
}