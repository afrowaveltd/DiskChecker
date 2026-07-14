$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs")
$found = $false
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match "GetSpeedSampleSeriesDownsampled|GetTestSessionWithoutSamples|AsNoTracking|AsTracking|Modulo|modulo") {
        $found = $true
        $start = [Math]::Max(0, $i - 5)
        $end = [Math]::Min($lines.Length - 1, $i + 30)
        for ($j = $start; $j -le $end; $j++) {
            Write-Host "$($j+1): $($lines[$j])"
        }
        Write-Host "---"
    }
}
if (-not $found) { Write-Host "No matches found" }
