$files = Get-ChildItem 'DiskChecker.UI.Avalonia\Views\*.axaml'
foreach ($file in $files) {
    $content = [IO.File]::ReadAllText($file.FullName)
    if ($content -match '\(\(vm:') {
        Write-Host "Found in: $($file.Name)"
        $matches = [regex]::Matches($content, '\(\(vm:[^)]+\)[^)]+\)')
        foreach ($m in $matches) {
            Write-Host "  - $($m.Value)"
        }
    }
}