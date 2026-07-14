$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs")
for ($i = 176; $i -lt 310; $i++) {
    Write-Host "$($i+1): $($lines[$i])"
}
