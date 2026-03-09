# Analyze QualityRating usage and fix Application errors

Write-Host "=== Analyzing QualityRating Usage ===" -ForegroundColor Green

# Check QualityRating definition in SmartaData.cs
Write-Host "`nQualityRating Definition in SmartaData.cs:" -ForegroundColor Yellow
$file = "DiskChecker.Core\Models\SmartaData.cs"
$lines = Get-Content $file -Encoding UTF8
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "enum QualityRating|public QualityGrade|extension.*QualityRating") {
        $lineNum = $i + 1
        Write-Host "$lineNum`: $($lines[$i])" -ForegroundColor Cyan
    }
}

# Check QualityRatingExtensions
Write-Host "`nQualityRatingExtensions Methods:" -ForegroundColor Yellow
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "public static.*GetGrade|public static.*GetScore|public static.*GetWarnings") {
        $lineNum = $i + 1
        Write-Host "$lineNum`: $($lines[$i])" -ForegroundColor Cyan
    }
}

# Check Application usage
Write-Host "`n=== Application Usage ===" -ForegroundColor Green

# DiskCheckerService.cs
$file1 = "DiskChecker.Application\Services\DiskCheckerService.cs"
$lines1 = Get-Content $file1 -Encoding UTF8
Write-Host "`nDiskCheckerService.cs Usage:" -ForegroundColor Yellow
for ($i = 0; $i -lt $lines1.Count; $i++) {
    if ($lines1[$i] -match "QualityRating|Grade|Score") {
        $lineNum = $i + 1
        Write-Host "$lineNum`: $($lines1[$i])" -ForegroundColor Cyan
    }
}