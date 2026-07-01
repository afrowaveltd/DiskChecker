$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs'
$lines = Get-Content $file
for ($i = 33; $i -le 36; $i++) {
    Write-Host "L$($i+1): $($lines[$i])"
}
