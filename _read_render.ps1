$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs")
for ($i = 310; $i -lt 370; $i++) {
    Write-Host "$($i+1): $($lines[$i])"
}
