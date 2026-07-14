
$file = "D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs"
$lines = Get-Content $file -Encoding UTF8
$total = $lines.Count
$partSize = [Math]::Ceiling($total / 3)
for ($i = 0; $i -lt 3; $i++) {
    $start = $i * $partSize
    $end = [Math]::Min(($i + 1) * $partSize - 1, $total - 1)
    $part = $lines[$start..$end]
    $part | Out-File -FilePath "C:\Users\lo505926\AppData\Local\Temp\cg_part_$i.txt" -Encoding UTF8
}
Write-Output "Total lines: $total, saved 3 parts"
