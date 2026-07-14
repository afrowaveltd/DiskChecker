$lines = [System.IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs")
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match "RenderCertificateJpeg|GeneratePdf|GenerateAndStore|jpg|JPEG|bitmap|SKBitmap|SKSurface|SKCanvas") {
        Write-Host "$($i+1): $($lines[$i])"
    }
}
