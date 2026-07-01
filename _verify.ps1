$file = 'D:\DiskChecker\DiskChecker.UI.Avalonia\Views\CertificateBrowserView.axaml'
$lines = Get-Content $file
for ($i = 0; $i -lt 15; $i++) {
    Write-Host "L$($i+1): $($lines[$i])"
}
Write-Host "---"
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'Polyline|PointsStringConverter|UserControl.Resources') {
        Write-Host "L$($i+1): $($lines[$i])"
    }
}
