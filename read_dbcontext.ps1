$path = "D:\DiskChecker\DiskChecker.Infrastructure\Persistence\DiskCheckerDbContext.cs"
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
for ($i = 0; $i -lt $lines.Length; $i++) {
    Write-Output ('{0:D4}:{1}' -f ($i+1), $lines[$i])
}
