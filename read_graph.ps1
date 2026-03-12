$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\SurfaceTestView.axaml", [Text.Encoding]::UTF8)
for ($i = 179; $i -lt 220 -and $i -lt $c.Count; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}