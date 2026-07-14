
$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = Get-Content $file -Encoding UTF8

# Extract GetSpeedSampleStallInfoAsync (line ~656)
for ($i = 655; $i -lt [Math]::Min(710, $lines.Count); $i++) {
    Write-Output "$($i): $($lines[$i])"
}
Write-Output "===END==="
