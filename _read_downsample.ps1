
$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = Get-Content $file -Encoding UTF8

# Extract lines 704-756 (LoadSpeedSeriesDownsampledAsync complete)
for ($i = 704; $i -lt [Math]::Min(757, $lines.Count); $i++) {
    Write-Output "$($i): $($lines[$i])"
}
