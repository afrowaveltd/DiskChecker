$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Show AddSpeedPoint method
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private void AddSpeedPoint") {
        Write-Output "Found at line $($i+1)"
        for ($j = $i; $j -lt [Math]::Min($i + 80, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}