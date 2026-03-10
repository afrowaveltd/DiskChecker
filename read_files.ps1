$files = @(
    "DiskChecker.UI.Avalonia\ViewModels\DiskSelectionViewModel.cs",
    "DiskChecker.UI.Avalonia\ViewModels\DiskStatusCardItem.cs"
)

foreach ($file in $files) {
    Write-Output "=== $file ==="
    $bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\$file")
    $encoding = [System.Text.Encoding]::UTF8
    $content = $encoding.GetString($bytes)
    Write-Output $content
    Write-Output ""
}