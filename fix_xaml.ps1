$files = @(
    'DiskChecker.UI.Avalonia\Views\DiskSelectionView.axaml',
    'DiskChecker.UI.Avalonia\Views\MainWindow.axaml',
    'DiskChecker.UI.Avalonia\Views\AnalysisView.axaml',
    'DiskChecker.UI.Avalonia\Views\HistoryView.axaml',
    'DiskChecker.UI.Avalonia\Views\ReportView.axaml',
    'DiskChecker.UI.Avalonia\Views\SettingsView.axaml',
    'DiskChecker.UI.Avalonia\Views\SmartCheckView.axaml',
    'DiskChecker.UI.Avalonia\Views\SurfaceTestView.axaml'
)

foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file)
    $content = $content -replace '\s*x:DataType="[^"]+"', ''
    [System.IO.File]::WriteAllText($file, $content)
    Write-Host "Fixed: $file"
}

Write-Host "Done!"