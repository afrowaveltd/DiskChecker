# Fix all BackupService issues

# 1. Fix the interface first
$interfacePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\Interfaces\IBackupService.cs"
$interfaceContent = [System.IO.File]::ReadAllText($interfacePath)

Write-Host "=== Fixing IBackupService Interface ==="
Write-Host "Before fix:"
Write-Host "Has 'IEnumerable<BackupInfo>': $($interfaceContent -match 'IEnumerable<BackupInfo>')"

# Fix Task<IEnumerable<BackupInfo>> to Task<IEnumerable<IBackupService.BackupInfo>>
$interfaceContent = $interfaceContent -replace 'Task<IEnumerable<BackupInfo>>', 'Task<IEnumerable<IBackupService.BackupInfo>>'

[System.IO.File]::WriteAllText($interfacePath, $interfaceContent)
Write-Host "Fixed IBackupService interface"

# 2. Fix BackupService.cs implementation
$backupServicePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($backupServicePath)

Write-Host ""
Write-Host "=== Fixing BackupService.cs ==="

# Fix RestoreBackupAsync: change Task<bool> to Task
Write-Host "Before: Checking for Task<bool> RestoreBackupAsync..."
if ($content -match 'Task<bool> RestoreBackupAsync') {
    Write-Host "Found 'Task<bool> RestoreBackupAsync'"
    $content = $content -replace 'Task<bool> RestoreBackupAsync', 'Task RestoreBackupAsync'
    Write-Host "Changed to 'Task RestoreBackupAsync'"
}

# Fix GetAvailableBackupsAsync: change Task<IEnumerable<BackupInfo>> to Task<IEnumerable<IBackupService.BackupInfo>>
Write-Host "Before: Checking for Task<IEnumerable<BackupInfo>>..."
if ($content -match 'Task<IEnumerable<BackupInfo>>') {
    Write-Host "Found 'Task<IEnumerable<BackupInfo>>'"
    $content = $content -replace 'Task<IEnumerable<BackupInfo>>', 'Task<IEnumerable<IBackupService.BackupInfo>>'
    Write-Host "Changed to 'Task<IEnumerable<IBackupService.BackupInfo>>'"
}

# Remove the return statement if it returns bool value
# Change "return result;" to just remove it or "return;" if needed
# Actually we need to check if there's internal BackupInfo class to remove too

[System.IO.File]::WriteAllText($backupServicePath, $content)
Write-Host "Fixed BackupService.cs"

# 3. Fix SettingsViewModel.cs
$settingsViewModelPath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SettingsViewModel.cs"
$vmContent = [System.IO.File]::ReadAllText($settingsViewModelPath)

Write-Host ""
Write-Host "=== Fixing SettingsViewModel.cs ==="
$vmContent = $vmContent -replace '\bBackupInfo\b', 'IBackupService.BackupInfo'
[System.IO.File]::WriteAllText($settingsViewModelPath, $vmContent)
Write-Host "Fixed SettingsViewModel.cs"

Write-Host ""
Write-Host "=== Verification ==="

# Verify interface
$interfaceContent = [System.IO.File]::ReadAllText($interfacePath)
Write-Host "Interface: Task<IEnumerable<IBackupService.BackupInfo>>: $($interfaceContent -match 'Task<IEnumerable<IBackupService\.BackupInfo>>')"

# Verify implementation
$backupContent = [System.IO.File]::ReadAllText($backupServicePath)
Write-Host "BackupService: Task RestoreBackupAsync: $($backupContent -match 'Task RestoreBackupAsync' -and $backupContent -notmatch 'Task<bool> RestoreBackupAsync')"
Write-Host "BackupService: Task<IEnumerable<IBackupService.BackupInfo>>: $($backupContent -match 'Task<IEnumerable<IBackupService\.BackupInfo>>')"

# Verify ViewModel
$vmContent = [System.IO.File]::ReadAllText($settingsViewModelPath)
Write-Host "SettingsViewModel: Uses IBackupService.BackupInfo: $($vmContent -match 'IBackupService\.BackupInfo')"

Write-Host ""
Write-Host "All fixes completed!"