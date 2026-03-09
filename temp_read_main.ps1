$utf8 = New-Object System.Text.UTF8Encoding $false
$content = [System.IO.File]::ReadAllText("D:\DiskChecker\DiskChecker.UI\Console\Pages\MainConsoleMenu.cs", $utf8)
$lines = $content -split "`n"
# Zobrazíme řádky 1-10 přesně
for ($i = 0; $i -lt 10 -and $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    Write-Output "$($i+1): [$($line)]"
}