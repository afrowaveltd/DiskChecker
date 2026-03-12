$c = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs", [Text.Encoding]::UTF8)
Write-Output "Total lines: $($c.Count)"
# Show first 60 lines
for ($i = 0; $i -lt [Math]::Min(60, $c.Count); $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $c[$i])
}