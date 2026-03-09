$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SurfaceTest\SurfaceTestViewModel.ProgressHandling.cs", $utf8)
$lines = $content -split "`n"
Write-Output "=== Lines 90-130 ==="
for ($i = 89; $i -lt 130 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}