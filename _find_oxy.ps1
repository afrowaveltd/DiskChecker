$views = Get-ChildItem 'D:\DiskChecker\DiskChecker.UI.Avalonia\Views\*.axaml'
foreach ($v in $views) {
    $c = Get-Content $v.FullName
    for ($i = 0; $i -lt $c.Count; $i++) {
        if ($c[$i] -match 'OxyPlot|oxy:') {
            $line = $c[$i].Trim()
            Write-Host "$($v.Name) L$($i+1): $line"
        }
    }
}
