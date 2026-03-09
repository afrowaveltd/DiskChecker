$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SmartCheck\SmartCheckViewModel.cs", $utf8)
$lines = $content -split "`n"
Write-Output "=== Lines 25-35 ==="
for ($i = 24; $i -lt 35 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}
Write-Output ""
Write-Output "=== Lines 440-450 ==="
for ($i = 439; $i -lt 450 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}
Write-Output ""
Write-Output "=== Lines 685-720 ==="
for ($i = 684; $i -lt 720 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}