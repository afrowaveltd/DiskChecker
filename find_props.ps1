$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find public properties
for ($i = 60; $i -lt 100; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}