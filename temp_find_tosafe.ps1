$utf8 = New-Object System.Text.UTF8Encoding $false

# Najít ToSafeString v TypeConversionExtensions
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

# Najít extension metody
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "ToSafeString") {
        Write-Output "$($i+1): $($lines[$i])"
    }
}