$path = "D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'DownsampleSpeed|DownsampleTemp|DownsampleStall|GetProfilePoints') {
        $start = [Math]::Max(0, $i - 1)
        $end = [Math]::Min($lines.Length - 1, $i + 25)
        for ($j = $start; $j -le $end; $j++) {
            Write-Output ('{0:D4}:{1}' -f ($j+1), $lines[$j])
        }
        Write-Output "---"
    }
}
