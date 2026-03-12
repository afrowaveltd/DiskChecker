$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\SurfaceTestView.axaml", [Text.Encoding]::UTF8)
# Find graph lines
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "Speed Graph|Graph Area|ItemsControl.*SpeedHistory") {
        Write-Output ("{0}: {1}" -f ($i+1), $c[$i])
    }
}