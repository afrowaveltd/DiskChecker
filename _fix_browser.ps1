$file = 'D:\DiskChecker\DiskChecker.UI.Avalonia\Views\CertificateBrowserView.axaml'
$content = Get-Content $file -Raw

# Add PointsStringConverter to Polyline bindings
$content = $content -replace '<Polyline Points="{Binding WriteProfilePoints}"', '<Polyline Points="{Binding WriteProfilePoints, Converter={StaticResource PointsStringConverter}}"'
$content = $content -replace '<Polyline Points="{Binding ReadProfilePoints}"', '<Polyline Points="{Binding ReadProfilePoints, Converter={StaticResource PointsStringConverter}}"'
$content = $content -replace '<Polyline Points="{Binding TemperatureProfilePoints}"', '<Polyline Points="{Binding TemperatureProfilePoints, Converter={StaticResource PointsStringConverter}}"'

Set-Content $file -Value $content -NoNewline
Write-Host "Done fixing CertificateBrowserView.axaml"
