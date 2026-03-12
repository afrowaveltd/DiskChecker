$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
for ($i = 430; $i -lt 490 -and $i -lt $c.Count; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}