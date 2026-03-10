$files = @(
    'DiskChecker.UI.Avalonia\Views\DiskSelectionView.axaml',
    'DiskChecker.UI.Avalonia\Views\SettingsView.axaml',
    'DiskChecker.UI.Avalonia\Views\MainWindow.axaml',
    'DiskChecker.UI.Avalonia\Views\AnalysisView.axaml',
    'DiskChecker.UI.Avalonia\Views\HistoryView.axaml',
    'DiskChecker.UI.Avalonia\Views\ReportView.axaml',
    'DiskChecker.UI.Avalonia\Views\SmartCheckView.axaml',
    'DiskChecker.UI.Avalonia\Views\SurfaceTestView.axaml'
)

foreach ($file in $files) {
    $content = [IO.File]::ReadAllText($file)
    $original = $content
    
    # Remove x:DataType attributes
    $content = $content -replace '\s*x:DataType="[^"]+"', ''
    
    # Fix cast bindings like ((vm:SettingsViewModel)DataContext) -> (DataContext)
    $content = $content -replace '\(\(vm:[^)]+\)([^)]+)\)', '($1)'
    
    # Fix single cast bindings like (vm:DiskSelectionViewModel)
    $content = $content -replace '\(vm:[^)]+\)', ''
    
    # Remove xmlns:vm declarations that are no longer needed (keep them for now)
    # $content = $content -replace '\s*xmlns:vm="[^"]+"', ''
    
    if ($content -ne $original) {
        [IO.File]::WriteAllText($file, $content)
        Write-Host "Fixed: $file"
    }
}

Write-Host "Done!"