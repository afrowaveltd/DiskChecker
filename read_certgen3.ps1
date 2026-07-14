$path = "D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
# Lines 260-350 (GeneratePdfAsync and surroundings)
for ($i = 259; $i -lt [Math]::Min(400, $lines.Length); $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
