$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.Core\Models\SmartaData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"

# Najít QualityRating definici
Write-Output "=== Looking for QualityRating definition ==="
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "QualityRating|QualityGrade") {
        Write-Output "$($i+1): $($lines[$i])"
    }
}