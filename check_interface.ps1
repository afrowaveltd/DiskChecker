# Check IBackupService interface
$interfacePath = "D:\DiskChecker\DiskChecker.UI.Avalonia\Services\Interfaces\IBackupService.cs"

if (Test-Path $interfacePath) {
    $content = [System.IO.File]::ReadAllText($interfacePath)
    Write-Host "=== IBackupService Interface ==="
    Write-Host $content
} else {
    Write-Host "Interface file not found at $interfacePath"
    Write-Host ""
    Write-Host "Searching for IBackupService..."
    Get-ChildItem -Path "D:\DiskChecker" -Recurse -Filter "IBackupService.cs" | ForEach-Object {
        Write-Host "Found: $($_.FullName)"
        $content = [System.IO.File]::ReadAllText($_.FullName)
        Write-Host $content
    }
}