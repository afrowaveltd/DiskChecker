$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

# Najít problém - řádek 285 má extra } mimo třídu
Write-Output "Lines count: $($lines.Count)"
Write-Output "=== Lines 270-287 ==="
for ($i = 269; $i -lt [Math]::Min(287, $lines.Count); $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}