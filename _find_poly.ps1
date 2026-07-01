$file = 'D:\DiskChecker\DiskChecker.UI.Avalonia\Views\CertificateBrowserView.axaml'
$lines = Get-Content $file
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'Polyline') {
        Write-Host "L$($i+1): $($lines[$i])"
    }
}
