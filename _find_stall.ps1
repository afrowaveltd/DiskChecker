$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs")
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match "GetSpeedSampleStallInfoAsync") {
        $end = [Math]::Min($lines.Length - 1, $i + 40)
        for ($j = $i; $j -le $end; $j++) {
            Write-Host "$($j+1): $($lines[$j])"
        }
    }
}
