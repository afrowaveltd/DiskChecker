$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find ZoomLevels property line
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "public ObservableCollection<GraphZoomLevel> ZoomLevels") {
        Write-Output "Found at line $($i+1)"
        for ($j = [Math]::Max(0, $i - 3); $j -lt [Math]::Min($i + 5, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}