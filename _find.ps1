$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs'
$lines = Get-Content $file
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'CertHeight|CertWidth') {
        Write-Host "Line $i : $($lines[$i])"
    }
}
