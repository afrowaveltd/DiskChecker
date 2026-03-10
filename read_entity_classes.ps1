# Find TestSession class
$testSessionPath = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\TestSession.cs"
$diskCardPath = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCard.cs"

Write-Output "=== TestSession.cs ==="
if (Test-Path $testSessionPath) {
    $bytes = [System.IO.File]::ReadAllBytes($testSessionPath)
    $encoding = [System.Text.Encoding]::UTF8
    $content = $encoding.GetString($bytes)
    Write-Output $content
}

Write-Output ""
Write-Output "=== DiskCard.cs ==="
if (Test-Path $diskCardPath) {
    $bytes = [System.IO.File]::ReadAllBytes($diskCardPath)
    $encoding = [System.Text.Encoding]::UTF8
    $content = $encoding.GetString($bytes)
    Write-Output $content
}