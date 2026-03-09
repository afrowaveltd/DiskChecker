$utf8 = New-Object System.Text.UTF8Encoding $false

# Zkontrolujme SmartaData definici
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

# Najít definici vlastností
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "DeviceModel|ModelFamily|PowerOnHours") {
        Write-Output "$($i+1): $($lines[$i])"
    }
}