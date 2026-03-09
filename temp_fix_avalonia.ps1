$utf8 = New-Object System.Text.UTF8Encoding $false

# Fix DiskSelectionViewModel.cs in Avalonia
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\DiskSelectionViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
if (-not ($content -match "using DiskChecker.Application.Models")) {
    $content = $content -replace "namespace DiskChecker.UI.Avalonia.ViewModels;", "using DiskChecker.Application.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;"
    [System.IO.File]::WriteAllText($filePath, $content, $utf8)
    Write-Output "DiskSelectionViewModel.cs fixed"
} else {
    Write-Output "DiskSelectionViewModel.cs already has using"
}

# Fix SettingsViewModel.cs in Avalonia - needs BackupInfo and ISettingsService
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SettingsViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$lines = $content -split "`n"
Write-Output "=== SettingsViewModel.cs first 20 lines ==="
for ($i = 0; $i -lt 20 -and $i -lt $lines.Count; $i++) {
    Write-Output "$($i+1): $($lines[$i])"
}