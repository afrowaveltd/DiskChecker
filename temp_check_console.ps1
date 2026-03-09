$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI\Console\Pages\MainConsoleMenu.cs", $utf8)
$lines = $content -split "`n"
Write-Output "=== First 15 lines ==="
for ($i = 0; $i -lt 15 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}