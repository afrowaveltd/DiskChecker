$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\Previous\DiskChecker.UI.WPF\ViewModels\Core\SettingsViewModel.cs", $utf8)
$lines = $content -split "`n"
for ($i = 0; $i -lt 50 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}