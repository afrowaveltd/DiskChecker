$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI.Avalonia\Views\DiskSelectionView.axaml", $utf8)
$lines = $content -split "`n"
for ($i = 0; $i -lt 30 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}