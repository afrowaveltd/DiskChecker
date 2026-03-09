# Verification script
$backupServicePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$settingsViewModelPath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SettingsViewModel.cs"

$backupContent = [System.IO.File]::ReadAllText($backupServicePath)
$vmContent = [System.IO.File]::ReadAllText($settingsViewModelPath)

Write-Host "=== BackupService.cs Check ==="
if ($backupContent -match 'async Task RestoreBackupAsync') {
    Write-Host "OK: RestoreBackupAsync returns Task"
} else {
    Write-Host "ERROR: RestoreBackupAsync fix not applied"
}

if ($backupContent -match 'Task<IEnumerable<IBackupService\.BackupInfo>>') {
    Write-Host "OK: GetAvailableBackupsAsync returns Task<IEnumerable<IBackupService.BackupInfo>>"
} else {
    Write-Host "ERROR: GetAvailableBackupsAsync fix not applied"
}

Write-Host ""
Write-Host "=== SettingsViewModel.cs Check ==="
if ($vmContent -match 'IBackupService\.BackupInfo') {
    Write-Host "OK: Uses IBackupService.BackupInfo"
} else {
    Write-Host "ERROR: BackupInfo not replaced"
}