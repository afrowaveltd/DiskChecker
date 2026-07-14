$path = "D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
# Show lines 1-100 (constants, constructor) and 900-930 (BuildImagePdfDocument)
Write-Output "=== LINES 1-100 ==="
for ($i = 0; $i -lt [Math]::Min(100, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
Write-Output ""
Write-Output "=== LINES 900-930 ==="
for ($i = 899; $i -lt [Math]::Min(930, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
