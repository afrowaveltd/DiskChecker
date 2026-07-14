$path = "D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
# Show BuildImagePdfDocument and DownsampleSpeedSamples
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'BuildImagePdfDocument|DownsampleSpeedSamples|DownsampleTemperatures|DownsampleStalls') {
        $start = [Math]::Max(0, $i - 1)
        $end = [Math]::Min($lines.Length - 1, $i + 30)
        Write-Output "=== $($lines[$i].Trim()) ==="
        for ($j = $start; $j -le $end; $j++) {
            Write-Output ('{0:D4}:{1}' -f ($j+1), $lines[$j])
        }
        Write-Output "---"
    }
}
