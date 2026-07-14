
$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = Get-Content $file -Encoding UTF8

# Extract GetTemperatureSampleSeriesDownsampledAsync (line ~584-650)
for ($i = 610; $i -lt [Math]::Min(655, $lines.Count); $i++) {
    Write-Output "$($i): $($lines[$i])"
}
Write-Output "===END==="
