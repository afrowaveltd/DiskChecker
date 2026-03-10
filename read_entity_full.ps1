$files = @(
    "DiskChecker.Core\Models\DiskCard.cs",
    "DiskChecker.Core\Models\TestSession.cs"
)

foreach ($file in $files) {
    $path = "D:\DiskChecker\$file"
    Write-Output "=== $file ==="
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $encoding = [System.Text.Encoding]::UTF8
    $content = $encoding.GetString($bytes)
    Write-Output $content
    Write-Output ""
}