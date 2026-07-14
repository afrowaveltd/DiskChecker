$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs'
$lines = Get-Content $file
for ($i = 220; $i -le 280; $i++) {
    if ($i -lt $lines.Count) {
        Write-Host "$($i+1): $($lines[$i])"
    }
}
