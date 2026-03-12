$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find SelectedZoomIndex setter
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "public int SelectedZoomIndex") {
        Write-Output "Found property at line $($i+1)"
        for ($j = $i; $j -lt [Math]::Min($i + 15, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}