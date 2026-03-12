$content = [IO.File]::ReadAllLines("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
# Show test execution and data collection
for ($i = 200; $i -lt 300 -and $i -lt $content.Count; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $content[$i])
}