$utf8 = New-Object System.Text.UTF8Encoding $false

# 1. Opravit LiveSmartDisplay.cs
$filePath = "D:\DiskChecker\DiskChecker.UI.Console\LiveSmartDisplay.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"
Write-Output "=== LiveSmartDisplay.cs Lines 130-175 ==="
for ($i = 129; $i -lt 175 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}