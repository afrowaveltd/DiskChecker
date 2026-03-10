$files = @(
    "DiskChecker.UI.Avalonia\ViewModels\DiskCardDetailViewModel.cs",
    "DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs"
)

foreach ($file in $files) {
    $path = "D:\DiskChecker\$file"
    if (Test-Path $path) {
        Write-Output "=== $file ==="
        $bytes = [System.IO.File]::ReadAllBytes($path)
        $encoding = [System.Text.Encoding]::UTF8
        $content = $encoding.GetString($bytes)
        # Find DbSet or SmartaData usage
        $lines = $content -split "`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match 'DbSet|SmartaData|_context') {
                Write-Output "$($i+1): $($lines[$i])"
            }
        }
        Write-Output ""
    }
}