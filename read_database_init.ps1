# Read DatabaseInitializationService and DB config
$files = @(
    "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DatabaseInitializationService.cs",
    "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DatabaseProviderConfiguration.cs",
    "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\ServiceCollectionExtensions.cs",
    "D:\DiskChecker\DiskChecker.Infrastructure\ServiceCollectionExtensions.cs"
)
foreach ($f in $files) {
    if (Test-Path $f) {
        Write-Output "=== $f ==="
        $lines = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)
        for ($i = 0; $i -lt $lines.Length; $i++) {
            Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
        }
        Write-Output ""
    }
}
