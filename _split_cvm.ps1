$lines = Get-Content 'D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\CertificateViewModel.cs'
$lines[0..199] | Out-File 'D:\DiskChecker\_cvm1.txt' -Encoding UTF8
$lines[200..399] | Out-File 'D:\DiskChecker\_cvm2.txt' -Encoding UTF8
$lines[400..599] | Out-File 'D:\DiskChecker\_cvm3.txt' -Encoding UTF8
$lines[600..799] | Out-File 'D:\DiskChecker\_cvm4.txt' -Encoding UTF8
$lines[800..999] | Out-File 'D:\DiskChecker\_cvm5.txt' -Encoding UTF8
$lines[1000..1199] | Out-File 'D:\DiskChecker\_cvm6.txt' -Encoding UTF8
$lines[1200..1399] | Out-File 'D:\DiskChecker\_cvm7.txt' -Encoding UTF8
Write-Host "Done"
