$files = @(
    "DiskChecker.UI.Avalonia\App.axaml.cs",
    "DiskChecker.UI.Avalonia\Program.cs",
    "DiskChecker.Infrastructure\Persistence\DiskCheckerDbContext.cs"
)

foreach ($file in $files) {
    Write-Output "=== $file ==="
    $bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\$file")
    $encoding = [System.Text.Encoding]::UTF8
    $content = $encoding.GetString($bytes)
    Write-Output $content
    Write-Output ""
}