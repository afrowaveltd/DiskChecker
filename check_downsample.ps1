$encoding = [System.Text.Encoding]::UTF8
$path = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = [System.IO.File]::ReadAllLines($path, $encoding)

# Show the key downsampling method
Write-Output "=== LoadSpeedSeriesDownsampledAsync (lines 739-810) ==="
for ($i = 738; $i -lt [Math]::Min(820, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
