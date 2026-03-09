$utf8 = New-Object System.Text.UTF8Encoding $false

# 1. Opravit SmartCheckViewModel.LoadInitialData.cs
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\SmartCheck\SmartCheckViewModel.LoadInitialData.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# PowerOnHours je int? - použít .GetValueOrDefault()
$content = $content -replace 'PowerOnHours = result\.SmartaData\.PowerOnHours,', 'PowerOnHours = result.SmartaData.PowerOnHours.GetValueOrDefault(),'

# Warnings je int - odstranit .Count
$content = $content -replace 'result\.Rating\.Warnings\.Count', 'result.Rating.Warnings'

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SmartCheckViewModel.LoadInitialData.cs updated"

# 2. Opravit DiskSelectionViewModel.cs
$filePath = "D:\DiskChecker\DiskChecker.UI.WPF\ViewModels\DiskSelection\DiskSelectionViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

$lines = $content -split "`n"
Write-Output "=== Lines 375-390 ==="
for ($i = 374; $i -lt 390 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}
Write-Output ""
Write-Output "=== Lines 710-725 ==="
for ($i = 709; $i -lt 725 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}