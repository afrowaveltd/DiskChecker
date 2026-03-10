# Read SurfaceTestView.axaml around line 135
$path = "D:\DiskChecker\DiskChecker.UI.Avalonia\Views\SurfaceTestView.axaml"
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
$lines = $content -split "`n"
for ($i = 130; $i -lt [Math]::Min(150, $lines.Count); $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}