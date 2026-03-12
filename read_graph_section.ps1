$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\SurfaceTestView.axaml", [Text.Encoding]::UTF8)
$startLine = 520
$endLine = [Math]::Min(600, $c.Count)
for ($i = $startLine; $i -lt $endLine; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}