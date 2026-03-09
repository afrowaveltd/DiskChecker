# Fix RestoreBackupAsync in BackupService.cs
$backupServicePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($backupServicePath)

Write-Host "Searching for RestoreBackupAsync patterns..."

# Find all lines containing RestoreBackupAsync
$lines = $content -split "`n"
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'RestoreBackupAsync') {
        Write-Host "Line $($i+1): $($lines[$i].Trim())"
    }
}

# Try different patterns
# Pattern 1: async void
if ($content -match 'async void RestoreBackupAsync') {
    Write-Host "Found 'async void RestoreBackupAsync', fixing..."
    $content = $content -replace 'async void RestoreBackupAsync', 'async Task RestoreBackupAsync'
    $fixed = $true
}
# Pattern 2: just void (without async)
elseif ($content -match 'void RestoreBackupAsync') {
    Write-Host "Found 'void RestoreBackupAsync', fixing..."
    $content = $content -replace 'public void RestoreBackupAsync', 'public async Task RestoreBackupAsync'
    $content = $content -replace 'public async void RestoreBackupAsync', 'public async Task RestoreBackupAsync'
    $fixed = $true
}
# Pattern 3: Task already there
elseif ($content -match 'Task RestoreBackupAsync') {
    Write-Host "RestoreBackupAsync already returns Task"
    $fixed = $false
}
else {
    Write-Host "Could not find RestoreBackupAsync pattern, checking file..."
    $fixed = $false
}

if ($fixed) {
    [System.IO.File]::WriteAllText($backupServicePath, $content)
    Write-Host "Fixed!"
}

# Show result
$content = [System.IO.File]::ReadAllText($backupServicePath)
if ($content -match 'Task RestoreBackupAsync') {
    Write-Host "Final: RestoreBackupAsync now returns Task"
} else {
    Write-Host "ERROR: Still not fixed"
}