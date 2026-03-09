# Fix CertificateGenerator.cs - remove QualityRatingExtensions calls

Write-Host "=== Fixing CertificateGenerator.cs ===" -ForegroundColor Green

$file = "DiskChecker.Core\Services\CertificateGenerator.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Looking for QualityRatingExtensions usage..." -ForegroundColor Yellow

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "QualityRatingExtensions") {
        $lineNum = $i + 1
        Write-Host "Line $lineNum`: $($lines[$i])" -ForegroundColor Cyan
        
        # Get context
        $start = [Math]::Max(0, $i - 2)
        $end = [Math]::Min($lines.Count - 1, $i + 2)
        Write-Host "Context:" -ForegroundColor Yellow
        for ($j = $start; $j -le $end; $j++) {
            $ctxLineNum = $j + 1
            Write-Host "  $ctxLineNum`: $($lines[$j])"
        }
    }
}

Write-Host "`n=== Fixing ===" -ForegroundColor Green

# Replace QualityRatingExtensions.GetGrade(rating) with rating.Grade
# Replace QualityRatingExtensions.GetScore(rating) with rating.Score
# Replace QualityRatingExtensions.GetWarnings(rating) with rating.Warnings

for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match "QualityRatingExtensions\.GetGrade") {
        $lines[$i] = $line -replace "QualityRatingExtensions\.GetGrade\(([^)]+)\)", '$1.Grade'
        Write-Host "Fixed line $($i + 1): $($lines[$i])" -ForegroundColor Green
    }
    if ($line -match "QualityRatingExtensions\.GetScore") {
        $lines[$i] = $line -replace "QualityRatingExtensions\.GetScore\(([^)]+)\)", '$1.Score'
        Write-Host "Fixed line $($i + 1): $($lines[$i])" -ForegroundColor Green
    }
    if ($line -match "QualityRatingExtensions\.GetWarnings") {
        $lines[$i] = $line -replace "QualityRatingExtensions\.GetWarnings\(([^)]+)\)", '$1.Warnings'
        Write-Host "Fixed line $($i + 1): $($lines[$i])" -ForegroundColor Green
    }
}

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`n=== CertificateGenerator.cs fixed ===" -ForegroundColor Green