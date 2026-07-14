$file = 'D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskCardDetailViewModel.cs'
$content = [System.IO.File]::ReadAllText($file, [System.Text.Encoding]::UTF8)
$lines = $content -split "`n"
$i = 0
foreach ($line in $lines) {
    $i++
    if ($line -match 'DIAG' -or $line -match 'GeneratingCert') {
        Write-Host "$i`: $line"
    }
}
Write-Host "Done"
