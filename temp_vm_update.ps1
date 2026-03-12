$content = [IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SurfaceTestViewModel.cs", [Text.Encoding]::UTF8)
$lines = $content -split "`n"
Write-Output "Total lines: $($lines.Count)"

# Find class declaration and first properties
for ($i = 0; $i -lt 80; $i++) {
    Write-Output ("{0,4}: {1}" -f ($i+1), $lines[$i])
}