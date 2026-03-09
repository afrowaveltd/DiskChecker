$files = Get-ChildItem -Path 'D:\DiskChecker\DiskChecker.UI.Avalonia\Services\Interfaces' -Filter '*.cs' -ErrorAction SilentlyContinue
Write-Output "=== Avalonia Service Interfaces ==="
foreach ($file in $files) {
    Write-Output $file.Name
}

$files = Get-ChildItem -Path 'D:\DiskChecker\DiskChecker.Application\Services' -Filter '*.cs' -ErrorAction SilentlyContinue
Write-Output ""
Write-Output "=== Application Services ==="
foreach ($file in $files) {
    Write-Output $file.Name
}