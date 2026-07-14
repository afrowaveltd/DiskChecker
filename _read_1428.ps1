$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs'
$lines = Get-Content $file
$total = $lines.Count
$start = 1410
$end = [Math]::Min(1460, $total - 1)

for ($i = $start; $i -le $end; $i++) {
    $lineNum = $i + 1
    Write-Host "$lineNum : $($lines[$i])"
}
Write-Host "---"
Write-Host "Total lines: $total"
