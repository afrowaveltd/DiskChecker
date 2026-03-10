$files = @(
    "DiskChecker.UI.Avalonia\ViewModels\DiskCardDetailViewModel.cs",
    "DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs",
    "DiskChecker.Infrastructure\Persistence\SmartaRecord.cs"
)

foreach ($file in $files) {
    Write-Output "=== $file ==="
    $bytes = [System.IO.File]::ReadAllBytes("D:\DiskChecker\$file")
    $encoding = [System.Text.Encoding]::UTF8
    $content = $encoding.GetString($bytes)
    Write-Output $content
    Write-Output ""
}