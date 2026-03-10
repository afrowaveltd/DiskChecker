# Find TestSession and other entity classes
$files = @(
    "DiskChecker.Infrastructure\Persistence\TestSession.cs",
    "DiskChecker.Infrastructure\Persistence\DiskCard.cs",
    "DiskChecker.Core\Models\SmartaData.cs"
)

foreach ($file in $files) {
    $path = "D:\DiskChecker\$file"
    if (Test-Path $path) {
        Write-Output "=== $file ==="
        $bytes = [System.IO.File]::ReadAllBytes($path)
        $encoding = [System.Text.Encoding]::UTF8
        $content = $encoding.GetString($bytes)
        Write-Output $content
        Write-Output ""
    }
}