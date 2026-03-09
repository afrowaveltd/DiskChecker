# Check QualityRating usage across Application services

Write-Host "=== QualityRating Usage in Application Services ===" -ForegroundColor Green

$files = @(
    "DiskChecker.Application\Services\DiskCheckerService.cs",
    "DiskChecker.Application\Services\SmartCheckService.cs",
    "DiskChecker.Application\Services\PdfReportExportService.cs",
    "DiskChecker.Application\Services\TestReportExportService.cs"
)

foreach ($file in $files) {
    $fileName = [System.IO.Path]::GetFileName($file)
    Write-Host "`n$fileName:" -ForegroundColor Yellow
    
    $lines = Get-Content $file -Encoding UTF8 -ErrorAction SilentlyContinue
    if ($lines) {
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match "QualityRating\.(Grade|Score|Warnings)|new QualityRating|\.Grade\s*=|\.Score\s*=|\.Warnings\s*=") {
                $lineNum = $i + 1
                Write-Host "  Line $lineNum`: $($lines[$i])" -ForegroundColor Cyan
            }
        }
    }
}

Write-Host "`n=== Analysis ===" -ForegroundColor Green
Write-Host "- Application code treats QualityRating as a class with properties" -ForegroundColor Red
Write-Host "- But Core defines QualityRating as an enum" -ForegroundColor Red
Write-Host "- Solutions: " -ForegroundColor Yellow
Write-Host "  1. Change Application to use enum values directly" -ForegroundColor Cyan
Write-Host "  2. Create a wrapper class in Application layer" -ForegroundColor Cyan