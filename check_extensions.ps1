# Check ServiceCollectionExtensions in Infrastructure
$extPath = "D:\DiskChecker\DiskChecker.Infrastructure\ServiceCollectionExtensions.cs"
if (Test-Path $extPath) {
    Write-Output "=== Infrastructure ServiceCollectionExtensions.cs ==="
    $content = [System.IO.File]::ReadAllText($extPath, [System.Text.Encoding]::UTF8)
    Write-Output $content
} else {
    Write-Output "File not found: $extPath"
}

Write-Output ""

# Also check persistence folder
$persistenceExt = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\ServiceCollectionExtensions.cs"
if (Test-Path $persistenceExt) {
    Write-Output "=== Persistence ServiceCollectionExtensions.cs ==="
    $content = [System.IO.File]::ReadAllText($persistenceExt, [System.Text.Encoding]::UTF8)
    Write-Output $content
}