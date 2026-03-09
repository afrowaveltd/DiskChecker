# Fix QualityRating readonly properties issue

Write-Host "=== Fixing DiskCheckerService.cs ===" -ForegroundColor Green

$file = "DiskChecker.Application\Services\DiskCheckerService.cs"
$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)

Write-Host "Line 28 (before): $($lines[27])" -ForegroundColor Cyan

# Fix: new QualityRating { Grade = ..., Score = ... } => new QualityRating(grade, score)
# The issue is that QualityRating is now a readonly struct, so we need to use the constructor
$lines[27] = '        return new QualityRating(QualityGrade.F, 0.0);'

Write-Host "Line 28 (after): $($lines[27])" -ForegroundColor Green

[System.IO.File]::WriteAllLines($file, $lines, [System.Text.Encoding]::UTF8)
Write-Host "`nFixed DiskCheckerService.cs - Line 28" -ForegroundColor Green