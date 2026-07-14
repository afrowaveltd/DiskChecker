$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs")
for ($i = 0; $i -lt 80; $i++) {
    Write-Host "$($i+1): $($lines[$i])"
}
