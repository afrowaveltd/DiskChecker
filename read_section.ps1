$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
for ($i = 199; $i -lt 350 -and $i -lt $c.Count; $i++) {
    $lineNum = $i + 1
    Write-Output ("{0,4}: {1}" -f $lineNum, $c[$i])
}