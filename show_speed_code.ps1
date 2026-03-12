$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Show SpeedHistory related code
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "SpeedHistory|AddSpeedSample|CurrentSpeed|MaxSpeed|MinSpeed|_testStartTime") {
        Write-Output ("Line {0}: {1}" -f ($i+1), $c[$i])
    }
}