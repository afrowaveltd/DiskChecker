
$file = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = Get-Content $file -Encoding UTF8

# Find ReadSpeedSamplesAsync and ColumnExistsAsync
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "(ReadSpeedSamplesAsync|ColumnExistsAsync|GetMatchingRowCountAsync)") {
        $start = [Math]::Max(0, $i - 2)
        $end = [Math]::Min($lines.Count - 1, $i + 25)
        for ($j = $start; $j -le $end; $j++) {
            Write-Output "$($j): $($lines[$j])"
        }
        Write-Output "=== MATCH ==="
    }
}
