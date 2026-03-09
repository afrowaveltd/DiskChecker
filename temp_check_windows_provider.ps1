$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Infrastructure\Hardware\WindowsSmartaProvider.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

# Najít problematické řádky
Write-Output "=== Lines 55-65 ==="
for ($i = 54; $i -lt 65 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}