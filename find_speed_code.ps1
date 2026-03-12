$c = [IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
$lines = $c -split "`n"
Write-Output "Total lines: $($lines.Count)"

# Find key sections
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "SpeedHistory|CurrentSpeed|MaxSpeed|_testStartTime|private.*speed|UpdateSpeedStats") {
        Write-Output ("Line {0}: {1}" -f ($i+1), $lines[$i].Trim())
    }
}