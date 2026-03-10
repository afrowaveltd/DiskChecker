$files = @(
    "DiskChecker.Infrastructure\Persistence\DiskCheckerDbContext.cs",
    "DiskChecker.Core\Models\SmartaData.cs"
)

foreach ($file in $files) {
    Write-Output "=== $file ==="
    $bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\$file")
    $encoding = [System.Text.Encoding]::UTF8
    $content = $encoding.GetString($bytes)
    Write-Output $content
    Write-Output ""
}