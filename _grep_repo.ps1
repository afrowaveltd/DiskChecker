
$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = Get-Content $file -Encoding UTF8
$total = $lines.Count
Write-Output "Total lines: $total"

# Get lines with "GetSpeedSample" or "GetTestSession" or "Downsample"
for ($i = 0; $i -lt $total; $i++) {
    if ($lines[$i] -match "(GetSpeedSample|GetTestSession|Downsample|GetTemperature)") {
        $start = [Math]::Max(0, $i - 2)
        $end = [Math]::Min($total - 1, $i + 5)
        for ($j = $start; $j -le $end; $j++) {
            Write-Output "$($j): $($lines[$j])"
        }
        Write-Output "---"
    }
}
