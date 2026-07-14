$encoding = [System.Text.Encoding]::UTF8
$path = "D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs"
if (Test-Path $path) {
    $lines = [System.IO.File]::ReadAllLines($path, $encoding)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
    }
} else {
    Write-Output "FILE NOT FOUND: $path"
}
