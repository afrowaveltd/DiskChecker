$path = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
# Find CreateCertificateAsync and surrounding context
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match 'CreateCertificate|SaveChart|ChartImage|ChartCache|\.Save|certificate\.(Chart|Jpeg|Image)' -or 
        $lines[$i] -match 'CertificateId|DiskCard_Certificates|Insert.*Certificate') {
        Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
    }
}
