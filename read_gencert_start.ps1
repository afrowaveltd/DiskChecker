$path = "D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
# Find GenerateCertificateAsync and show it
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'public.*DiskCertificate GenerateCertificateAsync|new DiskCertificate|TestSession\s*=|\.TestSession\s*=|DiskCard\s*=|\.DiskCard\s*=|certificate\.(TestSession|DiskCard)') {
        Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
    }
}
Write-Output ""
Write-Output "=== METHOD START (lines 230-320) ==="
for ($i = 229; $i -lt [Math]::Min(320, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
