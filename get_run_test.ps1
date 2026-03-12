$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Show lines around RunTestAsync
for ($i = 499; $i -lt 530 -and $i -lt $c.Count; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}