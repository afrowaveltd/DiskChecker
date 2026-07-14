# Check DLL timestamps
$paths = @(
    "D:\DiskChecker\DiskChecker.Infrastructure\bin\Release\net10.0\DiskChecker.Infrastructure.dll",
    "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCardRepository.cs",
    "D:\DiskChecker\DiskChecker.Infrastructure\bin\Debug\net10.0\DiskChecker.Infrastructure.dll"
)
foreach ($p in $paths) {
    if (Test-Path $p) {
        $info = Get-Item $p
        Write-Output "$($info.Name): $($info.LastWriteTime) ($($info.Length) bytes)"
    } else {
        Write-Output "NOT FOUND: $p"
    }
}
