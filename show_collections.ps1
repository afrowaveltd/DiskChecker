$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find WriteSpeedHistory property and add filtered versions
Write-Output "=== Current Speed History Properties ==="
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "public.*ObservableCollection.*(Speed|Temperature)History") {
        Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
    }
}

Write-Output ""
Write-Output "=== ZoomLevels property ==="
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "public.*ZoomLevels") {
        Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
    }
}