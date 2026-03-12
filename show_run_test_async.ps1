$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Find RunTestAsync method complete
for ($i = 0; $i -lt $c.Count; $i++) {
    if ($c[$i] -match "private async Task RunTestAsync") {
        for ($j = $i; $j -lt [Math]::Min($i + 80, $c.Count); $j++) {
            Write-Output ("{0,4}: {1}" -f ($j+1), $c[$j])
        }
        break
    }
}