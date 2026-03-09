$utf8 = New-Object System.Text.UTF8Encoding $false

# Opravit SettingsViewModel.cs
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SettingsViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Nahradit BackupInfo za IBackupService.BackupInfo
$content = $content -replace "ObservableCollection<BackupInfo>", "ObservableCollection<IBackupService.BackupInfo>"
$content = $content -replace "new ObservableCollection<BackupInfo>", "new ObservableCollection<IBackupService.BackupInfo>"
$content = $content -replace "List<BackupInfo>", "List<IBackupService.BackupInfo>"

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SettingsViewModel.cs fixed"