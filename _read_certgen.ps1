$lines = Get-Content 'D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs'
$lines[0..199] | Out-File 'D:\DiskChecker\_certgen_chunk1.txt' -Encoding UTF8
$lines[200..399] | Out-File 'D:\DiskChecker\_certgen_chunk2.txt' -Encoding UTF8
$lines[400..599] | Out-File 'D:\DiskChecker\_certgen_chunk3.txt' -Encoding UTF8
$lines[600..799] | Out-File 'D:\DiskChecker\_certgen_chunk4.txt' -Encoding UTF8
$lines[800..999] | Out-File 'D:\DiskChecker\_certgen_chunk5.txt' -Encoding UTF8
$lines[1000..1199] | Out-File 'D:\DiskChecker\_certgen_chunk6.txt' -Encoding UTF8
$lines[1200..1399] | Out-File 'D:\DiskChecker\_certgen_chunk7.txt' -Encoding UTF8
Write-Host "Done: $($lines.Count) lines"
