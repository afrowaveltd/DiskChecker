$utf8 = New-Object System.Text.UTF8Encoding $false

# 1. Opravit MainConsoleMenu.cs - přidat using
$filePath = "D:\DiskChecker\DiskChecker.UI\Console\Pages\MainConsoleMenu.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
if (-not ($content.Contains("using DiskChecker.Application.Models;"))) {
    $content = "using DiskChecker.Application.Models;" + [Environment]::NewLine + $content
    [System.IO.File]::WriteAllText($filePath, $content, $utf8)
    Write-Output "MainConsoleMenu.cs - using added"
} else {
    Write-Output "MainConsoleMenu.cs - already has using"
}

# 2. Opravit BackupService.cs - přidat BackupInfo třídu a správné návratové typy
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\BackupService.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)

# Najít konec souboru a přidat BackupInfo pokud tam není
if (-not ($content.Contains("public class BackupInfo"))) {
    # Přidat BackupInfo třídu na konec
    $backupInfoClass = @"

/// <summary>
/// Information about a backup file.
/// </summary>
public class BackupInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Version { get; set; } = "1.0.0";
}
"@
    $content = $content + $backupInfoClass
    [System.IO.File]::WriteAllText($filePath, $content, $utf8)
    Write-Output "BackupService.cs - BackupInfo class added"
}

# 3. Opravit SettingsViewModel.cs - použít IBackupService.BackupInfo
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SettingsViewModel.cs"
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
$content = $content -replace "ObservableCollection<BackupInfo>", "ObservableCollection<IBackupService.BackupInfo>"
$content = $content -replace "new ObservableCollection<BackupInfo>", "new ObservableCollection<IBackupService.BackupInfo>"
[System.IO.File]::WriteAllText($filePath, $content, $utf8)
Write-Output "SettingsViewModel.cs - BackupInfo qualified"

Write-Output "Done!"