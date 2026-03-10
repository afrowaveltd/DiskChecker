# Check DbContext initialization and database creation
$files = @(
    "DiskChecker.UI.Avalonia\App.axaml.cs",
    "DiskChecker.Infrastructure\Persistence\DiskCheckerDbContext.cs"
)

foreach ($file in $files) {
    $path = "D:\DiskChecker\$file"
    Write-Output "=== $file ==="
    $content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    Write-Output $content
    Write-Output ""
}