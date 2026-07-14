
$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = Get-Content $file -Encoding UTF8

# Extract lines 709-780 (LoadSpeedSeriesDownsampledAsync)
for ($i = 708; $i -lt [Math]::Min(790, $lines.Count); $i++) {
    Write-Output "$($i): $($lines[$i])"
}
Write-Output "===END==="

# Extract lines 551-610 (GetSpeedSampleSeriesDownsampledAsync)
Write-Output "=== GetSpeedSampleSeriesDownsampledAsync ==="
for ($i = 550; $i -lt [Math]::Min(615, $lines.Count); $i++) {
    Write-Output "$($i): $($lines[$i])"
}
Write-Output "===END==="
