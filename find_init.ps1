$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find _testStartTime and _currentPhase initialization
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "_testStartTime|_currentPhase|ClearSpeedHistory") {
        Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
    }
}