$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\SurfaceTestView.axaml", [Text.Encoding]::UTF8)
# Find the graph area section
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "<!-- Speed Graph|<!-- Graph Area|Border Grid.Row") {
        Write-Output "Found graph area at line $($i+1)"
        # Show next 40 lines
        for ($j = $i; $j -lt [Math]::Min($i + 50, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}