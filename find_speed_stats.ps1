$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find where DisplayMaxSpeed is calculated
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "DisplayMaxSpeed|AvgSpeed =|MaxSpeed =|MinSpeed =") {
        Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
    }
}