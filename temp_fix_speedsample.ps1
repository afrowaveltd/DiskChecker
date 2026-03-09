$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\Models\SpeedSample.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Opravíme namespace
$newContent = $content -replace "namespace DiskChecker.UI.WPF.ViewModels;", "namespace DiskChecker.UI.WPF.Models;"
[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "SpeedSample namespace fixed"