$utf8 = New-Object System.Text.UTF8Encoding $false
$prevPath = "D:\DiskChecker\Previous\DiskChecker.Core\Models\SmartaData.cs"
$prevContent = [System.IO.File]::ReadAllText($prevPath, $utf8)
$prevLines = $prevContent -split "`n"

# Najít QualityRating definici v Previous
Write-Output "=== QualityRating in Previous ==="
for ($i = 0; $i -lt $prevLines.Count; $i++) {
    if ($prevLines[$i] -match "QualityRating|public static QualityRating") {
        Write-Output "$($i+1): $($prevLines[$i])"
    }
}