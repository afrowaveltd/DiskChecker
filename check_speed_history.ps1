$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find SpeedHistory
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "SpeedHistory|_speedHistory") {
        Write-Output ("Line {0}: {1}" -f ($i+1), $c[$i])
    }
}