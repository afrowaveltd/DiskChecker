$files = Get-ChildItem 'D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\*.cs'
foreach ($f in $files) {
    $c = Get-Content $f.FullName
    for ($i = 0; $i -lt $c.Count; $i++) {
        if ($c[$i] -match 'OxyPlot') {
            $line = $c[$i].Trim()
            Write-Host "$($f.Name) L$($i+1): $line"
        }
    }
}
