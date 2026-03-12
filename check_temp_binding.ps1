$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\SurfaceTestView.axaml", [Text.Encoding]::UTF8)
# Find Temperature graph binding
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "TemperatureHistory|VisibleTemperatureData") {
        Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
    }
}