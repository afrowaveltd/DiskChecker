$content = [System.IO.File]::ReadAllText("D:\DiskChecker\errors2.txt", [System.Text.Encoding]::UTF8)
$lines = $content -split "`n"
# Zobrazíme prvních 50 chyb
for ($i = 0; $i -lt [Math]::Min(50, $lines.Count); $i++) {
    Write-Output $lines[$i]
}