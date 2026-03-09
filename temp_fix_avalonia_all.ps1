$utf8 = New-Object System.Text.UTF8Encoding $false

# 1. Opravit SettingsViewModel - potřebuje BackupInfo z IBackupService
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SettingsViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Přidáme using pro Application Services
$newContent = $content -replace "using DiskChecker.UI.Avalonia.Services.Interfaces;", "using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.Application.Services;"

# Předěláváme ISettingsService na HistoryService (neexistuje)
$newContent = $newContent -replace "private readonly ISettingsService", "private readonly HistoryService"
$newContent = $newContent -replace "ISettingsService settingsService", "HistoryService historyService"
$newContent = $newContent -replace "_settingsService", "_historyService"
$newContent = $newContent -replace "ISettingsService", "HistoryService"

# BackupInfo musíme kvalifikovat přes IBackupService
$newContent = $newContent -replace "ObservableCollection<BackupInfo>", "ObservableCollection[IBackupService.BackupInfo]"
$newContent = $newContent -replace "new ObservableCollection<BackupInfo>", "new ObservableCollection[IBackupService.BackupInfo]"
$newContent = $newContent -replace "\[IBackupService.BackupInfo\]", "<IBackupService.BackupInfo>"

[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "SettingsViewModel.cs fixed"


# 2. Opravit ReportViewModel
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\ReportViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# IReportService neexistuje - musíme použít HistoryService
$newContent = $content -replace "IReportService", "HistoryService"
$newContent = $newContent -replace "_reportService", "_historyService"
$newContent = $newContent -replace "using DiskChecker.UI.Avalonia.Services.Interfaces;", "using DiskChecker.UI.Avalonia.Services.Interfaces;
using DiskChecker.Application.Models;
using DiskChecker.Application.Services;"

[System.IO.File]::WriteAllText($filePath, $newContent, $utf8)
Write-Output "ReportViewModel.cs fixed"