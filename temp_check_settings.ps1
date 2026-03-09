$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SettingsViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"
Write-Output "=== Lines 18-25 ==="
for ($i = 17; $i -lt 25 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}
Write-Output ""
Write-Output "=== Lines 128-135 ==="
for ($i = 127; $i -lt 135 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}