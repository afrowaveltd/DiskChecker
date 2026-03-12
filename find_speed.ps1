$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find SpeedHistory and surrounding context
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "SpeedHistory") {
        Write-Output "Found at line $($i+1)"
        # Show context around it
        for ($j = [Math]::Max(0, $i-5); $j -lt [Math]::Min($c.Count, $i+10); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}