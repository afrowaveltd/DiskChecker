$file = 'D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs'
$content = Get-Content $file -Raw
$content = $content -replace 'private const int CertHeight = 1754;', 'private const int CertHeight = 2200;'
$content = $content -replace '// Certificate JPEG dimensions \(A4 at 150 DPI\)', '// Certificate JPEG dimensions (A4 at 150 DPI, extended for all content)'
Set-Content $file -Value $content -NoNewline
Write-Host "Done"
