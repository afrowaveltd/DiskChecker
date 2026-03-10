# Check Core IServiceCollectionExtensions
$coreExt = "D:\DiskChecker\DiskChecker.Core\IServiceCollectionExtensions.cs"
if (Test-Path $coreExt) {
    Write-Output "=== Core IServiceCollectionExtensions.cs ==="
    $content = [System.IO.File]::ReadAllText($coreExt, [System.Text.Encoding]::UTF8)
    Write-Output $content
}