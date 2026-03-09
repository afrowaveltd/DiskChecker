# PowerShell script to fix BackupService issues
# Run this script to apply fixes

Write-Host "Reading BackupService.cs..."
$backupServicePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($backupServicePath)

Write-Host "Before changes:"
Write-Host "Contains 'async void RestoreBackupAsync': $($content -match 'async void RestoreBackupAsync')"
Write-Host "Contains 'Task<IEnumerable<BackupInfo>>': $($content -match 'Task<IEnumerable<BackupInfo>>' -and $content -notmatch 'IBackupService\.BackupInfo')"

# Change RestoreBackupAsync return type from void to Task
$content = $content -replace 'public async void RestoreBackupAsync', 'public async Task RestoreBackupAsync'

# Change GetAvailableBackupsAsync return type
$content = $content -replace 'public async Task<IEnumerable<BackupInfo>> GetAvailableBackupsAsync', 'public async Task<IEnumerable<IBackupService.BackupInfo>> GetAvailableBackupsAsync'

[System.IO.File]::WriteAllText($backupServicePath, $content)
Write-Host "Fixed BackupService.cs"

Write-Host ""
Write-Host "Reading SettingsViewModel.cs..."
$settingsViewModelPath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SettingsViewModel.cs"
$content = [System.IO.File]::ReadAllText($settingsViewModelPath)

Write-Host "Before changes:"
Write-Host "Contains standalone 'BackupInfo': $($content -match '\bBackupInfo\b' -and $content -notmatch 'IBackupService\.BackupInfo')"

# Replace all occurrences of BackupInfo with IBackupService.BackupInfo
# But not IBackupService.BackupInfo (avoid double replacement)
$content = $content -replace '\bBackupInfo\b', 'IBackupService.BackupInfo'

[System.IO.File]::WriteAllText($settingsViewModelPath, $content)
Write-Host "Fixed SettingsViewModel.cs"

Write-Host ""
Write-Host "All fixes applied successfully!"

# Verification
Write-Host ""
Write-Host "=== Verification ==="
$backupContent = [System.IO.File]::ReadAllText($backupServicePath)
$vmContent = [System.IO.File]::ReadAllText($settingsViewModelPath)

Write-Host "BackupService.cs:"
Write-Host "  - Has 'async Task RestoreBackupAsync': $($backupContent -match 'async Task RestoreBackupAsync')"
Write-Host "  - Has 'Task<IEnumerable<IBackupService.BackupInfo>>': $($backupContent -match 'Task<IEnumerable<IBackupService\.BackupInfo>>')"

Write-Host "SettingsViewModel.cs:"
Write-Host "  - Uses 'IBackupService.BackupInfo': $($vmContent -match 'IBackupService\.BackupInfo')"