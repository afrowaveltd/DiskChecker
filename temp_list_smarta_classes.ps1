$utf8 = New-Object System.Text.UTF8Encoding $false

# Zkontrolujme SmartaData.cs - kolik řádek má
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"
Write-Output "SmartaData.cs has $($lines.Count) lines"

# Najít všechny třídy v souboru
Write-Output ""
Write-Output "=== Classes in SmartaData.cs ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^public (class|record|struct|enum|interface)") {
        Write-Output "$($i+1): $($lines[$i])"
    }
}