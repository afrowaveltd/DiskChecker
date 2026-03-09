$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI\Console\Pages\MainConsoleMenu.cs", $utf8)
$lines = $content -split "`n"
for ($i = 1385; $i -lt 1400 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}