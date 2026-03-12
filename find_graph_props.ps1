$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find UpdateGraphHeights
$found = $false
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private void UpdateGraphHeights") {
        Write-Output "Found at line $($i+1)"
        for ($j = $i; $j -lt [Math]::Min($i + 25, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        $found = $true
        break
    }
}
if (-not $found) {
    Write-Output "UpdateGraphHeights not found"
}

# Find DisplayMaxSpeed property
Write-Output ""
Write-Output "--- DisplayMaxSpeed ---"
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "DisplayMaxSpeed|DisplayMaxTemperature") {
        Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
    }
}