$utf8 = New-Object System.Text.UTF8Encoding $false

# Opravit IBackupService.cs - přidat BackupInfo třídu a upravit návratové typy
$filePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\Interfaces\IBackupService.cs"

# Zjistíme současný obsah
$content = [System.IO.File]::ReadAllText($filePath, $utf8)
Write-Output "=== Current IBackupService.cs ==="
Write-Output $content