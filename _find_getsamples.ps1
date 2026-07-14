$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs")
# Find GetSpeedSampleSeriesDownsampled and GetTestSessionWithoutSamples
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match "public async.*GetSpeedSampleSeriesDownsampled|public async.*GetTestSessionWithoutSamples") {
        $end = [Math]::Min($lines.Length - 1, $i + 80)
        for ($j = $i; $j -le $end; $j++) {
            Write-Host "$($j+1): $($lines[$j])"
        }
    }
}
