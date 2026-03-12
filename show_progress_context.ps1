$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\SmartCheckView.axaml", [Text.Encoding]::UTF8)
# Show context around the progress indicator (lines 530-560)
for ($i = 529; $i -lt 560; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}