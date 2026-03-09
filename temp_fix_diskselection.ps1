$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\DiskSelection\DiskSelectionViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# 1. Řádek 379: quality: null as QualityRating - QualityRating je struct, použít QualityRating?
$content = $content -replace 'quality: null as QualityRating', 'quality: null as QualityRating?'

# 2. Řádek 717: quality.Warnings.Count -> quality.Warnings
$content = $content -replace 'quality\.Warnings\.Count', 'quality.Warnings'

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "DiskSelectionViewModel.cs updated"