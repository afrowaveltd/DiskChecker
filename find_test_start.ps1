$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find where SpeedHistory.Clear() is called (test start)
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "ClearSpeedHistory|_testStartTime = |IsTesting = true") {
        Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
    }
}