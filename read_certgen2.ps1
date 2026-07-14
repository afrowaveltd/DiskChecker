$path = "D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
# Lines 640-900 (0-based 639-899) - BuildChartImageAsync and PDF generation
for ($i = 639; $i -lt [Math]::Min(900, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
