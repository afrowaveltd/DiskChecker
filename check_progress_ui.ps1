$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\SmartCheckView.axaml", [Text.Encoding]::UTF8)
# Show the progress indicator section fully
for ($i = 535; $i -lt 550; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}