$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs")
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match "GenerateCertificate|Downsample|WriteProfile|ReadProfile|SpeedSamples|SmartAttr|BuildImage") {
        $start = [Math]::Max(0, $i - 3)
        $end = [Math]::Min($lines.Length - 1, $i + 10)
        for ($j = $start; $j -le $end; $j++) {
            Write-Host "$($j+1): $($lines[$j])"
        }
        Write-Host "---"
    }
}
