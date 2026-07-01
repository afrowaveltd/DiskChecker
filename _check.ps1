$c = Get-Content 'D:\DiskChecker\DiskChecker.UI.Avalonia\Views\CertificateBrowserView.axaml'
$c[160..175] | Out-File 'D:\DiskChecker\_check1.txt' -Encoding UTF8

$c2 = Get-Content 'D:\DiskChecker\DiskChecker.UI.Avalonia\Views\CertificateView.axaml'
$c2[100..120] | Out-File 'D:\DiskChecker\_check2.txt' -Encoding UTF8

$c3 = Get-Content 'D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\CertificateViewModel.cs'
$c3 | Select-String 'TestDuration' | Out-File 'D:\DiskChecker\_check3.txt' -Encoding UTF8

$c4 = Get-Content 'D:\DiskChecker\DiskChecker.Infrastructure\Services\CertificateGenerator.cs'
$c4 | Select-String 'TestDuration' | Out-File 'D:\DiskChecker\_check4.txt' -Encoding UTF8

$c5 = Get-Content 'D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\CertificateViewModel.cs'
$c5 | Select-String 'Duration' | Out-File 'D:\DiskChecker\_check5.txt' -Encoding UTF8

Write-Host "Done"
