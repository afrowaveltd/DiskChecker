$utf8 = New-Object System.Text.UTF8Encoding $false
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SettingsViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Opravit duplicitní IBackupService.IBackupService.IBackupService na IBackupService
$content = $content -replace "IBackupService\.IBackupService\.IBackupService\.BackupInfo", "IBackupService.BackupInfo"
$content = $content -replace "IBackupService\.IBackupService\.BackupInfo", "IBackupService.BackupInfo"

[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SettingsViewModel.cs fixed"