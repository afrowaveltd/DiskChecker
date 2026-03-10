# Check ViewLocator and App.axaml.cs initialization
$files = @(
    "DiskChecker.UI.Avalonia\ViewLocator.cs",
    "DiskChecker.UI.Avalonia\App.axaml.cs"
)

foreach ($file in $files) {
    $path = "D:\DiskChecker\$file"
    Write-Output "=== $file ==="
    $content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    Write-Output $content
    Write-Output ""
}