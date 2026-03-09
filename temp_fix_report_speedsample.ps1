$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\Core\ReportViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Opravíme typ SpeedSample na SurfaceTestSample
$newContent = $content -replace "DiskChecker\.Core\.Models\.SpeedSample", "SurfaceTestSample"
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "SpeedSample type fixed to SurfaceTestSample"